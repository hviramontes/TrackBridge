using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Xml.Linq;
using System.Globalization;
using TrackBridge;
using TrackBridge.CoT;

namespace TrackBridge.Tests
{
    [TestClass]
    public class CotBuilderTests
    {
        [TestMethod]
        public void BuildCotXml_WithCustomIconAndCountry_IncludesCorrectAttributes()
        {
            // Arrange
            var track = new EntityTrack
            {
                Id = 42,
                Lat = 12.34,
                Lon = 56.78,
                Alt = 90.12,
                CustomMarking = "Alpha",
                CountryCode = "GBR",
                IconType = "icon123",
                Domain = 1,       // Land
                EntityKind = 1    // Typically vehicle
            };

            // Act
            string xml = CotBuilder.BuildCotXml(track);
            var doc = XDocument.Parse(xml);
            var root = doc.Root;

            // Assert: top‐level <event>
            Assert.AreEqual("event", root.Name.LocalName);
            Assert.AreEqual("2.0", root.Attribute("version")?.Value);
            Assert.AreEqual("TrackBridge-42", root.Attribute("uid")?.Value);
            Assert.AreEqual("icon123", root.Attribute("type")?.Value);
            Assert.AreEqual("m-g", root.Attribute("how")?.Value);
            Assert.IsNotNull(root.Attribute("time"));
            Assert.IsNotNull(root.Attribute("start"));
            Assert.IsNotNull(root.Attribute("stale"));

            // Assert: <point> coords
            var point = root.Element("point");
            Assert.IsNotNull(point);
            double lat = double.Parse(point.Attribute("lat").Value, CultureInfo.InvariantCulture);
            double lon = double.Parse(point.Attribute("lon").Value, CultureInfo.InvariantCulture);
            double hae = double.Parse(point.Attribute("hae").Value, CultureInfo.InvariantCulture);
            Assert.AreEqual(12.34, lat, 0.0001);
            Assert.AreEqual(56.78, lon, 0.0001);
            Assert.AreEqual(90.12, hae, 0.0001);

            // Assert: <contact> callsign
            var contact = root.Element("detail")?.Element("contact");
            Assert.IsNotNull(contact);
            Assert.AreEqual("Alpha", contact.Attribute("callsign")?.Value);

            // Assert: <group> attributes
            var group = root.Element("detail")?.Element("group");
            Assert.IsNotNull(group);
            Assert.AreEqual("ground", group.Attribute("role")?.Value);      // Domain=1 → ground
            Assert.AreEqual("GBR", group.Attribute("country")?.Value);
            Assert.AreEqual("icon123", group.Attribute("iconType")?.Value);
        }

        [TestMethod]
        public void BuildCotXml_WithoutIconType_UsesFallbackType()
        {
            // Arrange: no IconType → fallback to Cot type based on EntityKind/Domain
            var track = new EntityTrack
            {
                Id = 100,
                Lat = 0,
                Lon = 0,
                Alt = 0,
                CustomMarking = null,
                CountryCode = null,
                IconType = "",       // triggers fallback
                Domain = 2,          // Air
                EntityKind = 1
            };

            // Act
            string xml = CotBuilder.BuildCotXml(track);
            var root = XDocument.Parse(xml).Root;

            // Assert: type="a-h-A" for Air (entityKind=1, domain=2)
            Assert.AreEqual("a-h-A", root.Attribute("type")?.Value);
        }
    }
}
