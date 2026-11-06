using iucs.readernest.application.Services;
using Xunit;

namespace iucs.readernest.tests
{
    public class ClassroomPresenceTrackerTests
    {
        [Fact]
        public void TeacherStaysPresent_ForTheGraceWindow_AfterHerConnectionBlinks()
        {
            // Live, 6 Nov 2026: children were held at "Waiting for the teacher" for minutes while the
            // teacher was in class, because her classroom connection had dropped for a moment.
            var tracker = new ClassroomPresenceTracker();
            tracker.UserJoined("s1", "c1", Guid.NewGuid(), "Teacher", "teacher");
            Assert.True(tracker.IsPresentOrRecentlyLeft("s1", "teacher", TimeSpan.FromMinutes(5)));

            tracker.UserLeft("s1", "c1");

            Assert.True(tracker.IsPresentOrRecentlyLeft("s1", "teacher", TimeSpan.FromMinutes(5)));
            Assert.False(tracker.IsPresentOrRecentlyLeft("s1", "teacher", TimeSpan.Zero));
        }

        [Fact]
        public void NoTeacherYet_OrOnlyAStudent_IsNotPresent()
        {
            var tracker = new ClassroomPresenceTracker();
            Assert.False(tracker.IsPresentOrRecentlyLeft("s1", "teacher", TimeSpan.FromMinutes(5)));

            tracker.UserJoined("s1", "c2", Guid.NewGuid(), "Child", "student");
            tracker.UserLeft("s1", "c2");

            Assert.False(tracker.IsPresentOrRecentlyLeft("s1", "teacher", TimeSpan.FromMinutes(5)));
        }
    }
}
