// ReplayLogicTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;
using TrackBridge;
using System.Runtime.Serialization;
using TrackBridge.CoT;

namespace TrackBridge.Tests
{
    [TestClass]
    public class ReplayLogicTests
    {
        private MethodInfo _parseLogLine;
        private object _mainWindow;

        [TestInitialize]
        public void Init()
        {
            var mwType = typeof(MainWindow);
            // Allocate MainWindow instance without running its constructor
            _mainWindow = FormatterServices.GetUninitializedObject(mwType);
            _parseLogLine = mwType.GetMethod(
                "ParseLogLine",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
        }


        [TestMethod]
        public void ParseLogLine_ValidXmlLine_ReturnsCorrectTuple()
        {
            // Arrange
            var now = DateTime.UtcNow;
            string xmlLine = $"<event time=\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\"/>";

            // Act
            var result = _parseLogLine.Invoke(_mainWindow, new object[] { xmlLine });
            var time = (DateTime)result
                .GetType().GetField("Item1").GetValue(result);
            var xml = (string)result
                .GetType().GetField("Item2").GetValue(result);

            // Assert
            // Allow a small millisecond tolerance on the parsed time
            var diff = (time - now).Duration();
            Assert.IsTrue(diff < TimeSpan.FromMilliseconds(1),
                $"Parsed time {time:o} differs from expected {now:o} by {diff.TotalMilliseconds}ms");

            Assert.AreEqual(xmlLine, xml);
        }

        [TestMethod]
        public void LogEntries_AreOrderedByTime()
        {
            // Arrange
            var now = DateTime.UtcNow;
            var events = new[] {
                (time: now.AddSeconds(5), xml: "Second"),
                (time: now,               xml: "First")
            };

            // Act
            var ordered = events.OrderBy(e => e.time).ToArray();

            // Assert
            Assert.AreEqual("First", ordered[0].xml);
            Assert.AreEqual("Second", ordered[1].xml);
        }
    }
}
