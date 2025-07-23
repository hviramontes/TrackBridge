using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using TrackBridge;

namespace TrackBridge.Tests
{
    [TestClass]
    public class FilterLogicTests
    {
        // Mirror your app’s defaults
        private static readonly string[] DefaultDomains = { "1", "2", "3", "4", "5" };
        private static readonly string[] DefaultKinds = { "Neutral", "Friendly", "Hostile", "Unknown" };

        [TestMethod]
        public void MergeDomains_ReturnsDistinctSorted()
        {
            // Arrange: simulate seen domains
            var dynamic = new[] { "3", "2", "7", "1", "2" };

            // Act: merge defaults + dynamic
            var all = DefaultDomains
                .Union(dynamic)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            // Assert: expect 1-5 plus 7, sorted
            var expected = new[] { "1", "2", "3", "4", "5", "7" };
            CollectionAssert.AreEqual(expected, all);
        }

        [TestMethod]
        public void MergeKinds_ReturnsDistinctSorted()
        {
            // Arrange: simulate seen kinds
            var dynamic = new[] { "Alpha", "Friendly", "Charlie" };

            // Act: merge defaults + dynamic
            var all = DefaultKinds
                .Union(dynamic)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            // Assert: alphabetical merged list
            var expected = new[] { "Alpha", "Charlie", "Friendly", "Hostile", "Neutral", "Unknown" };
            CollectionAssert.AreEqual(expected, all);
        }
    }
}
