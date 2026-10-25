using System.Reflection;
using System.Security.Claims;
using iucs.readernest.api.Hubs;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace iucs.readernest.tests
{
    /// <summary>
    /// The live classroom across a dropped connection: a child's tablet (or the API itself)
    /// blinking must not cost the class its whiteboard, its lasting annotations, the board access
    /// the teacher gave, or a raised hand -- and must not hand out a second star.
    /// Each "connection" below is a separate hub instance with its own connection id, exactly as
    /// SignalR creates them; a reconnect is the same person joining again under a new id.
    /// </summary>
    public class ClassroomHubRealtimeTests
    {
        private readonly List<(string To, string Method, object?[] Args)> _sent = new();

        // A personal room (session id == the host's own account id) and guest-link students need no
        // database: the hub authorizes both from the token alone.
        private readonly Guid _teacherId = Guid.NewGuid();
        private string SessionId => _teacherId.ToString();

        private ClassroomHub Connect(string connectionId, Guid userId, bool guest)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
            if (guest)
            {
                claims.Add(new Claim("purpose", "guest-classroom"));
                claims.Add(new Claim("sessionId", SessionId));
            }

            return new ClassroomHub(
                Stub<ISessionService>(), Stub<IGamificationService>(), Stub<IAcademicOpsService>(),
                Stub<IClassroomPresenceTracker>(), Stub<IClassSessionEventLogService>())
            {
                Context = new FakeContext(connectionId, new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))),
                Clients = Recording<IHubCallerClients>(connectionId),
                Groups = Stub<IGroupManager>(),
            };
        }

        private async Task<ClassroomHub> JoinTeacherAsync(string connectionId = "t1")
        {
            var hub = Connect(connectionId, _teacherId, guest: false);
            await hub.JoinSession(SessionId, "Teacher");
            return hub;
        }

        private async Task<ClassroomHub> JoinStudentAsync(string connectionId, Guid userId, string name = "Asha")
        {
            var hub = Connect(connectionId, userId, guest: true);
            await hub.JoinSession(SessionId, name);
            return hub;
        }

        private List<(string Method, object?[] Args)> SentTo(string target) =>
            _sent.Where(m => m.To == target).Select(m => (m.Method, m.Args)).ToList();

        [Fact]
        public async Task BoardAccess_SurvivesTheStudentsReconnect_AndTheRosterSaysSo()
        {
            var teacher = await JoinTeacherAsync();
            var asha = Guid.NewGuid();
            var student = await JoinStudentAsync("s1", asha);
            await teacher.SetBoardAccess(SessionId, "s1", true);

            await student.OnDisconnectedAsync(null); // tablet drops...
            _sent.Clear();
            var back = await JoinStudentAsync("s2", asha); // ...and comes back under a new connection id

            Assert.Contains(SentTo("caller:s2"), m => m.Method == "BoardAccess" && Equals(m.Args[0], true));
            await back.SendBoard(SessionId, """{"kind":"stroke"}""");
            Assert.Contains(_sent, m => m.Method == "Board" && m.To.StartsWith("othersInGroup:"));

            var roster = _sent.Last(m => m.Method == "Roster").Args[0]!;
            var rows = ((System.Collections.IEnumerable)roster).Cast<object>().ToList();
            var ashaRow = rows.Single(r => (string)r.GetType().GetProperty("name")!.GetValue(r)! == "Asha");
            Assert.True((bool)ashaRow.GetType().GetProperty("boardAccess")!.GetValue(ashaRow)!);

            // Taking it back still works against the new connection.
            await teacher.SetBoardAccess(SessionId, "s2", false);
            _sent.Clear();
            await back.SendBoard(SessionId, """{"kind":"stroke"}""");
            Assert.DoesNotContain(_sent, m => m.Method == "Board");
        }

        [Fact]
        public async Task Annotation_RequiresItsOwnGrant_AndRevocationIsEnforcedServerSide()
        {
            var teacher = await JoinTeacherAsync();
            var studentId = Guid.NewGuid();
            var student = await JoinStudentAsync("annotation-student", studentId);
            const string mark = """{"kind":"stroke","stroke":{"id":"m1","points":[{"x":0.4,"y":0.4}],"color":"red","width":4}}""";

            _sent.Clear();
            await student.SendAnnotation(SessionId, mark);
            Assert.DoesNotContain(_sent, m => m.Method == "Annotation");

            // Whiteboard permission must not implicitly permit drawing over a presentation.
            await teacher.SetBoardAccess(SessionId, "annotation-student", true);
            _sent.Clear();
            await student.SendAnnotation(SessionId, mark);
            Assert.DoesNotContain(_sent, m => m.Method == "Annotation");

            await teacher.SetAnnotationAccess(SessionId, "annotation-student", true);
            _sent.Clear();
            await student.SendAnnotation(SessionId, mark);
            Assert.Contains(_sent, m => m.Method == "Annotation" && m.To.StartsWith("othersInGroup:"));

            await teacher.SetAnnotationAccess(SessionId, "annotation-student", false);
            _sent.Clear();
            await student.SendAnnotation(SessionId, mark);
            Assert.DoesNotContain(_sent, m => m.Method == "Annotation");
        }

        [Fact]
        public async Task AnnotationAccess_SurvivesStudentReconnect()
        {
            var teacher = await JoinTeacherAsync();
            var studentId = Guid.NewGuid();
            var student = await JoinStudentAsync("annotation-first", studentId);
            await teacher.SetAnnotationAccess(SessionId, "annotation-first", true);

            await student.OnDisconnectedAsync(null);
            _sent.Clear();
            var reconnected = await JoinStudentAsync("annotation-back", studentId);

            Assert.Contains(SentTo("caller:annotation-back"), m => m.Method == "AnnotationAccess" && Equals(m.Args[0], true));
            const string mark = """{"kind":"clear"}""";
            await reconnected.SendAnnotation(SessionId, mark);
            Assert.Contains(_sent, m => m.Method == "Annotation" && m.To.StartsWith("othersInGroup:"));
        }

        [Fact]
        public async Task GoogleSlidesLink_IsTeacherOnlyValidatedAndReplayedToLateJoiners()
        {
            var teacher = await JoinTeacherAsync();
            var student = await JoinStudentAsync("slides-student", Guid.NewGuid());
            const string embed = "https://docs.google.com/presentation/d/deck-id/embed?start=false";

            _sent.Clear();
            await student.SetPresentationLink(SessionId, embed);
            await teacher.SetPresentationLink(SessionId, "https://example.com/not-allowed");
            Assert.DoesNotContain(_sent, m => m.Method == "PresentationLink");

            await teacher.SetPresentationLink(SessionId, embed);
            Assert.Contains(_sent, m => m.Method == "PresentationLink" && Equals(m.Args[0], embed));

            _sent.Clear();
            await JoinStudentAsync("slides-late", Guid.NewGuid(), "Late");
            Assert.Contains(SentTo("caller:slides-late"), m => m.Method == "PresentationLink" && Equals(m.Args[0], embed));
        }

        [Fact]
        public async Task MalformedAnnotation_IsNotRelayedEvenFromAnAuthorizedParticipant()
        {
            var teacher = await JoinTeacherAsync();
            _sent.Clear();

            await teacher.SendAnnotation(SessionId, """{"kind":"stroke","stroke":{"id":"broken"}}""");

            Assert.DoesNotContain(_sent, m => m.Method == "Annotation");
        }

        [Fact]
        public async Task LateJoiner_GetsTheBoardAndLastingAnnotations_WithAResetOnlyWhenThereIsSomethingToRebuild()
        {
            var teacher = await JoinTeacherAsync();
            _sent.Clear();
            await JoinStudentAsync("early", Guid.NewGuid(), "Early");
            // Nothing drawn yet (which is also how it looks after the API restarts mid-class):
            // a client must not be told to wipe a board the server can't rebuild.
            Assert.DoesNotContain(SentTo("caller:early"), m => m.Method is "BoardReset" or "Annotation");

            const string mark = """{"kind":"stroke","stroke":{"id":"m1","points":[{"x":0.4,"y":0.4}],"color":"red","width":4,"persistent":true}}""";
            const string fading = """{"kind":"stroke","stroke":{"id":"m2","points":[{"x":0.5,"y":0.5}],"color":"red","width":4}}""";
            await teacher.SendBoard(SessionId, """{"kind":"stroke","id":"b1"}""");
            await teacher.SendAnnotation(SessionId, mark);
            await teacher.SendAnnotation(SessionId, fading);

            _sent.Clear();
            await JoinStudentAsync("late", Guid.NewGuid(), "Late");
            var toLate = SentTo("caller:late").Where(m => m.Method is "BoardReset" or "Board" or "Annotation").ToList();
            Assert.Equal(["BoardReset", "Board", "Annotation", "Annotation"], toLate.Select(m => m.Method));
            Assert.Equal("""{"kind":"clear"}""", toLate[2].Args[0]);
            Assert.Equal(mark, toLate[3].Args[0]); // the lasting mark; the fading one is not replayed
        }

        [Fact]
        public async Task Reconnecting_DoesNotEarnASecondStar_AndKeepsARaisedHand()
        {
            await JoinTeacherAsync();
            var asha = Guid.NewGuid();
            var student = await JoinStudentAsync("s1", asha);
            await student.AnswerQuiz(SessionId, questionIndex: 0, selectedIndex: 1, correct: true);
            await student.RaiseHand(SessionId, true);

            await student.OnDisconnectedAsync(null);
            _sent.Clear();
            var back = await JoinStudentAsync("s2", asha);
            await back.AnswerQuiz(SessionId, questionIndex: 0, selectedIndex: 1, correct: true);
            Assert.DoesNotContain(_sent, m => m.Method == "QuizAnswer");

            var rows = ((System.Collections.IEnumerable)_sent.Last(m => m.Method == "Roster").Args[0]!).Cast<object>().ToList();
            var ashaRow = rows.Single(r => (string)r.GetType().GetProperty("name")!.GetValue(r)! == "Asha");
            Assert.True((bool)ashaRow.GetType().GetProperty("handRaised")!.GetValue(ashaRow)!);
        }

        [Fact]
        public async Task AStudent_CannotGiveThemselvesStarsInABurst()
        {
            await JoinTeacherAsync();
            var student = await JoinStudentAsync("s1", Guid.NewGuid());
            _sent.Clear();

            for (var i = 0; i < 20; i++)
            {
                await student.AwardStar(SessionId);
            }

            var board = ((System.Collections.IEnumerable)_sent.Last(m => m.Method == "Leaderboard").Args[0]!).Cast<object>().ToList();
            var asha = board.Single(r => (string)r.GetType().GetProperty("name")!.GetValue(r)! == "Asha");
            Assert.Equal(1, (int)asha.GetType().GetProperty("stars")!.GetValue(asha)!);
        }

        [Fact]
        public async Task Chat_IsCutToAReasonableLength()
        {
            var teacher = await JoinTeacherAsync();
            _sent.Clear();
            await teacher.SendChat(SessionId, new string('x', 5000));
            Assert.Equal(500, ((string)_sent.Single(m => m.Method == "Chat").Args[1]!).Length);
        }

        // ---- fakes: just enough of SignalR to run a hub method and see what it sent to whom ----

        private T Recording<T>(string connectionId) where T : class
        {
            var proxy = DispatchProxy.Create<T, RecordingClients>();
            var recording = (RecordingClients)(object)proxy;
            recording.ConnectionId = connectionId;
            recording.Sent = _sent;
            return proxy;
        }

        private static T Stub<T>() where T : class => DispatchProxy.Create<T, NoOp>();

        public class NoOp : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                var type = method!.ReturnType;
                if (type == typeof(Task)) return Task.CompletedTask;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    var inner = type.GetGenericArguments()[0];
                    // ResolveCurrentSessionIdAsync and friends are never reached on the token-only
                    // join paths these tests use; a default is enough for everything else.
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner)
                        .Invoke(null, [inner.IsValueType ? Activator.CreateInstance(inner) : null]);
                }

                return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
            }
        }

        public class RecordingClients : DispatchProxy
        {
            public string ConnectionId = "";
            public List<(string To, string Method, object?[] Args)> Sent = null!;

            protected override object? Invoke(MethodInfo? method, object?[]? args)
            {
                var name = method!.Name.Replace("get_", string.Empty);
                var target = name switch
                {
                    "Caller" => $"caller:{ConnectionId}",
                    "Client" => $"caller:{args![0]}",
                    "OthersInGroup" => $"othersInGroup:{args![0]}",
                    "Group" => $"group:{args![0]}",
                    _ => name,
                };
                return new Proxy(target, Sent);
            }
        }

        private sealed class Proxy(string target, List<(string To, string Method, object?[] Args)> sent) : ISingleClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                sent.Add((target, method, args));
                return Task.CompletedTask;
            }

            public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default) =>
                Task.FromResult(default(T)!);
        }

        private sealed class FakeContext(string connectionId, ClaimsPrincipal user) : HubCallerContext
        {
            public override string ConnectionId { get; } = connectionId;
            public override string? UserIdentifier => null;
            public override ClaimsPrincipal? User { get; } = user;
            public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
            public override IFeatureCollection Features { get; } = new FeatureCollection();
            public override CancellationToken ConnectionAborted => CancellationToken.None;
            public override void Abort() { }
        }
    }
}
