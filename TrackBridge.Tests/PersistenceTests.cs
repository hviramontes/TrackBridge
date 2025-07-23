using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Newtonsoft.Json;
using TrackBridge;

namespace TrackBridge.Tests
{
    [TestClass]
    public class PersistenceTests
    {
        [TestMethod]
        public void FilterSettings_SerializeDeserialize_RetainsValues()
        {
            // Arrange
            var original = new FilterSettings
            {
                AllowedDomains = new List<string> { "1", "2" },
                AllowedKinds = new List<string> { "Friendly", "Hostile" }
            };

            // Act
            string json = JsonConvert.SerializeObject(original);
            var roundTrip = JsonConvert.DeserializeObject<FilterSettings>(json);

            // Assert
            CollectionAssert.AreEqual(original.AllowedDomains, roundTrip.AllowedDomains);
            CollectionAssert.AreEqual(original.AllowedKinds, roundTrip.AllowedKinds);
        }

        [TestMethod]
        public void ProfileDictionary_SerializeDeserialize_RetainsEntries()
        {
            // Arrange
            var fs1 = new FilterSettings
            {
                AllowedDomains = new List<string> { "1", "2" },
                AllowedKinds = new List<string> { "Friendly" }
            };
            var profiles = new Dictionary<string, FilterSettings>
            {
                ["ProfileA"] = fs1
            };

            // Act
            string json = JsonConvert.SerializeObject(profiles);
            var roundTrip = JsonConvert.DeserializeObject<Dictionary<string, FilterSettings>>(json);

            // Assert
            Assert.IsTrue(roundTrip.ContainsKey("ProfileA"));
            CollectionAssert.AreEqual(
                fs1.AllowedDomains,
                roundTrip["ProfileA"].AllowedDomains);
            CollectionAssert.AreEqual(
                fs1.AllowedKinds,
                roundTrip["ProfileA"].AllowedKinds);
        }
    }
}
