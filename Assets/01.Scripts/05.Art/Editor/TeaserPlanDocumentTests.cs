// System
using System;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Geumeum.Art.Editor.Tests
{
    public sealed class TeaserPlanDocumentTests
    {
        private string _path;
        private string _original;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "geumeum-teaser-" + Guid.NewGuid().ToString("N") + ".json");
            _original = File.ReadAllText(TeaserPlanDocument.ASSET_PATH);
            File.WriteAllText(_path, _original);
        }

        [TearDown]
        public void TearDown()
        {
            File.Delete(_path);
        }

        [Test]
        public void PoseRoundtripPreservesEditorialDataAndOtherShots()
        {
            TeaserPlanDocument document = TeaserPlanDocument.Load(_path);
            JToken overlays = document.Root["overlays"].DeepClone();
            JToken second = document.Shots[1].DeepClone();
            document.SavePose(0, true, new Vector3(1f, 2f, 3f), Vector3.up, 45f, (string)document.Shots[0]["scene"]);
            TeaserPlanDocument saved = TeaserPlanDocument.Load(_path);
            Assert.AreEqual(45f, (float)saved.Shots[0]["cameraFrom"]["fov"]);
            Assert.IsTrue(JToken.DeepEquals(overlays, saved.Root["overlays"]));
            Assert.IsTrue(JToken.DeepEquals(second, saved.Shots[1]));
        }

        [Test]
        public void RejectsExternalChangesWithoutOverwritingThem()
        {
            TeaserPlanDocument document = TeaserPlanDocument.Load(_path);
            string external = _original + "\n";
            File.WriteAllText(_path, external);
            Assert.Throws<IOException>(() => document.SavePose(0, true, Vector3.zero, Vector3.zero, 45f, (string)document.Shots[0]["scene"]));
            Assert.AreEqual(external, File.ReadAllText(_path));
        }

        [Test]
        public void RejectsCameraFromDifferentScene()
        {
            TeaserPlanDocument document = TeaserPlanDocument.Load(_path);
            Assert.Throws<InvalidOperationException>(() => document.SavePose(0, true, Vector3.zero, Vector3.zero, 45f, "Assets/00.Scenes/Release/Boot.unity"));
            Assert.AreEqual(_original, File.ReadAllText(_path));
        }

        [Test]
        public void RejectsAmbiguousFramesAndGameplaySlowdown()
        {
            JObject root = JObject.Parse(_original);
            root["shots"][0]["durationFrames"] = 240.5;
            Assert.Throws<InvalidDataException>(() => TeaserPlanDocument.Parse(root.ToString()));
            root = JObject.Parse(_original);
            root["shots"][5]["timeScale"] = 0.25;
            Assert.Throws<InvalidDataException>(() => TeaserPlanDocument.Parse(root.ToString()));
        }
    }
}
