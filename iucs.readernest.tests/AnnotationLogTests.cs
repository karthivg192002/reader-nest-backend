using iucs.readernest.api.Hubs;
using Xunit;

namespace iucs.readernest.tests
{
    /// <summary>What a late joiner is sent so their screen matches the class's lasting annotations.</summary>
    public class AnnotationLogTests
    {
        private const string Lasting = """{"kind":"stroke","stroke":{"id":"a","points":[{"x":0.1,"y":0.1}],"color":"red","width":4,"persistent":true,"space":"share"}}""";
        private const string LastingAppend = """{"kind":"strokeAppend","strokeId":"a","points":[{"x":0.2,"y":0.2}]}""";
        private const string Temporary = """{"kind":"stroke","stroke":{"id":"t","points":[{"x":0.5,"y":0.5}],"color":"red","width":4}}""";
        private const string TemporaryAppend = """{"kind":"strokeAppend","strokeId":"t","points":[{"x":0.6,"y":0.6}]}""";

        [Fact]
        public void KeepsLastingMarksInOrder_AndNeverTemporaryInk()
        {
            var log = new AnnotationLog();
            log.Record(Temporary);
            log.Record(Lasting);
            log.Record(TemporaryAppend);
            log.Record(LastingAppend);

            Assert.Equal([Lasting, LastingAppend], log.Snapshot());
        }

        [Fact]
        public void TurningThePenOffKeepsLastingMarks_ButClearRemovesThem()
        {
            var log = new AnnotationLog();
            log.Record(Lasting);

            log.Record("""{"kind":"clear","temporaryOnly":true}""");
            Assert.Single(log.Snapshot());

            log.Record("""{"kind":"clear"}""");
            Assert.Empty(log.Snapshot());

            // An append for a mark that was cleared has nothing to extend.
            log.Record(LastingAppend);
            Assert.Empty(log.Snapshot());
        }

        [Fact]
        public void IgnoresWhatItCannotRead_AndStopsGrowingAtItsLimit()
        {
            var log = new AnnotationLog();
            log.Record("not json");
            log.Record("[1,2,3]");
            log.Record("""{"kind":"stroke"}""");
            Assert.Empty(log.Snapshot());

            log.Record(Lasting);
            for (var i = 0; i < AnnotationLog.MaxOps + 50; i++)
            {
                log.Record(LastingAppend);
            }

            Assert.Equal(AnnotationLog.MaxOps, log.Snapshot().Length);
        }
    }
}
