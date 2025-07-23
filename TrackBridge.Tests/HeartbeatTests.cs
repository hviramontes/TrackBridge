using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;
using TrackBridge.CoT;

namespace TrackBridge.Tests
{
    [TestClass]
    public class HeartbeatTests
    {
        [TestMethod]
        public void BuildPingCot_ProducesValidXmlWithContactCallsign()
        {
            // Act
            string xml = CotBuilder.BuildPingCot();
            var doc = XDocument.Parse(xml);
            var root = doc.Root;

            // Assert root element
            Assert.AreEqual("event", root.Name.LocalName);
            Assert.AreEqual("2.0", root.Attribute("version")?.Value);

            // Check detail/contact callsign
            var contact = root.Element("detail")?.Element("contact");
            Assert.IsNotNull(contact);
            Assert.AreEqual("TrackBridge", contact.Attribute("callsign")?.Value);
        }

        [TestMethod]
        public void BuildPingCot_HasValidPointAttributes()
        {
            // Act
            string xml = CotBuilder.BuildPingCot();
            var point = XDocument.Parse(xml).Root.Element("point");

            // Assert required attributes
            Assert.IsNotNull(point.Attribute("lat"));
            Assert.IsNotNull(point.Attribute("lon"));
            Assert.IsNotNull(point.Attribute("hae"));
            Assert.IsNotNull(point.Attribute("ce"));
            Assert.IsNotNull(point.Attribute("le"));
        }
    }
}
