using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.XREAL.Samples;

namespace DentalNavigation.Tests
{
    public sealed class DentalDisplayLayoutAnchorTests
    {
        const string PrefPrefix = "DentalNavigation.Layout.v2.";

        static readonly string[] FloatPrefKeys =
        {
            PrefPrefix + "hud.x",
            PrefPrefix + "hud.y",
            PrefPrefix + "hud.z",
            PrefPrefix + "model.x",
            PrefPrefix + "model.y",
            PrefPrefix + "model.z",
        };

        static readonly string[] IntPrefKeys =
        {
            PrefPrefix + "hud.visible",
            PrefPrefix + "model.visible",
        };

        static readonly string[] AnchorPrefKeysThatMustNotExist =
        {
            PrefPrefix + "anchor",
            PrefPrefix + "contentAnchor",
            PrefPrefix + "contentAnchorMode",
            PrefPrefix + "hover",
        };

        GameObject m_Host;
        DentalDisplayLayoutController m_Layout;
        Dictionary<string, float> m_SavedFloats;
        Dictionary<string, int> m_SavedInts;
        Dictionary<string, bool> m_HadKey;

        [SetUp]
        public void SetUp()
        {
            SnapshotPrefs();
            DestroyLeftovers();
            m_Host = new GameObject("DentalDisplayLayoutAnchorTests");
            m_Layout = m_Host.AddComponent<DentalDisplayLayoutController>();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyLeftovers();
            RestorePrefs();
        }

        [Test]
        public void NewControllerStartsInFollowMode()
        {
            Assert.That(m_Layout.ContentAnchorMode, Is.EqualTo(DentalContentAnchorMode.FollowHead));
        }

        [Test]
        public void UnknownEnumValuesAreIgnoredWithoutNotification()
        {
            var notifications = 0;
            m_Layout.Changed += () => notifications++;

            m_Layout.SetContentAnchorModeLocally((DentalContentAnchorMode)99);

            Assert.That(m_Layout.ContentAnchorMode, Is.EqualTo(DentalContentAnchorMode.FollowHead));
            Assert.That(notifications, Is.EqualTo(0));
        }

        [Test]
        public void ChangingModeNotifiesOnceAndIgnoresRedundantSets()
        {
            var notifications = 0;
            m_Layout.Changed += () => notifications++;

            m_Layout.SetContentAnchorModeLocally(DentalContentAnchorMode.WorldLocked);
            m_Layout.SetContentAnchorModeLocally(DentalContentAnchorMode.WorldLocked);
            Assert.That(m_Layout.ContentAnchorMode, Is.EqualTo(DentalContentAnchorMode.WorldLocked));
            Assert.That(notifications, Is.EqualTo(1));

            m_Layout.SetContentAnchorModeLocally(DentalContentAnchorMode.FollowHead);
            m_Layout.SetContentAnchorModeLocally(DentalContentAnchorMode.FollowHead);
            Assert.That(m_Layout.ContentAnchorMode, Is.EqualTo(DentalContentAnchorMode.FollowHead));
            Assert.That(notifications, Is.EqualTo(2));
        }

        [Test]
        public void AnchorModeIsNotWrittenToPlayerPrefs()
        {
            var prefsBefore = CaptureKnownPrefs();

            m_Layout.SetContentAnchorModeLocally(DentalContentAnchorMode.WorldLocked);

            Assert.That(CaptureKnownPrefs(), Is.EqualTo(prefsBefore));
            foreach (var key in AnchorPrefKeysThatMustNotExist)
                Assert.That(PlayerPrefs.HasKey(key), Is.False, "Did not expect PlayerPrefs key {0}.", key);
        }

        [Test]
        public void RecreatedControllerDoesNotRestoreAPreviousHoverMode()
        {
            m_Layout.SetContentAnchorModeLocally(DentalContentAnchorMode.WorldLocked);
            Object.DestroyImmediate(m_Host);
            m_Host = null;

            m_Host = new GameObject("DentalDisplayLayoutAnchorTests.Recreated");
            m_Layout = m_Host.AddComponent<DentalDisplayLayoutController>();

            Assert.That(m_Layout.ContentAnchorMode, Is.EqualTo(DentalContentAnchorMode.FollowHead));
        }

        [Test]
        public void AnchorModeDoesNotChangeControlVersionOrRemoteLayoutFields()
        {
            var remote = new DentalDisplayLayoutState(
                "session-a",
                4,
                7,
                DentalControlSource.NavigationSoftware,
                true,
                true,
                new Vector3(0.1f, -0.2f, 1.5f),
                new Vector3(0.3f, -0.05f, 1.6f),
                false);
            Assert.That(m_Layout.ApplyRemote(remote), Is.True);
            Assert.That(m_Layout.ControlVersion, Is.EqualTo(7UL));
            Assert.That(m_Layout.ContentAnchorMode, Is.EqualTo(DentalContentAnchorMode.FollowHead));

            m_Layout.SetContentAnchorModeLocally(DentalContentAnchorMode.WorldLocked);

            Assert.That(m_Layout.ControlVersion, Is.EqualTo(7UL));
            Assert.That(m_Layout.HudVisible, Is.True);
            Assert.That(m_Layout.ModelVisible, Is.True);
            Assert.That((m_Layout.HudLocalPositionMeters - remote.HudPositionMeters).sqrMagnitude,
                Is.LessThan(0.00000001f));
            Assert.That((m_Layout.ModelLocalPositionMeters - remote.ModelPositionMeters).sqrMagnitude,
                Is.LessThan(0.00000001f));
            Assert.That(m_Layout.ContentAnchorMode, Is.EqualTo(DentalContentAnchorMode.WorldLocked));
        }

        [Test]
        public void ApplyRemoteDoesNotOverrideLocalAnchorMode()
        {
            m_Layout.SetContentAnchorModeLocally(DentalContentAnchorMode.WorldLocked);
            var remote = new DentalDisplayLayoutState(
                "session-b",
                1,
                2,
                DentalControlSource.NavigationSoftware,
                false,
                false,
                new Vector3(0f, -0.13f, 1.8f),
                new Vector3(0.32f, -0.024f, 1.8f),
                false);

            Assert.That(m_Layout.ApplyRemote(remote), Is.True);
            Assert.That(m_Layout.ContentAnchorMode, Is.EqualTo(DentalContentAnchorMode.WorldLocked));
            Assert.That(m_Layout.HudVisible, Is.False);
        }

        void DestroyLeftovers()
        {
            if (m_Host != null)
            {
                Object.DestroyImmediate(m_Host);
                m_Host = null;
                m_Layout = null;
            }

            if (DentalDisplayLayoutController.Instance != null)
                Object.DestroyImmediate(DentalDisplayLayoutController.Instance.gameObject);
            if (DentalNavigationState.Instance != null)
                Object.DestroyImmediate(DentalNavigationState.Instance.gameObject);
        }

        void SnapshotPrefs()
        {
            m_HadKey = new Dictionary<string, bool>();
            m_SavedFloats = new Dictionary<string, float>();
            m_SavedInts = new Dictionary<string, int>();
            foreach (var key in FloatPrefKeys)
            {
                m_HadKey[key] = PlayerPrefs.HasKey(key);
                if (m_HadKey[key])
                    m_SavedFloats[key] = PlayerPrefs.GetFloat(key);
            }

            foreach (var key in IntPrefKeys)
            {
                m_HadKey[key] = PlayerPrefs.HasKey(key);
                if (m_HadKey[key])
                    m_SavedInts[key] = PlayerPrefs.GetInt(key);
            }
        }

        void RestorePrefs()
        {
            foreach (var key in FloatPrefKeys)
            {
                if (m_HadKey != null && m_HadKey.ContainsKey(key) && m_HadKey[key])
                    PlayerPrefs.SetFloat(key, m_SavedFloats[key]);
                else
                    PlayerPrefs.DeleteKey(key);
            }

            foreach (var key in IntPrefKeys)
            {
                if (m_HadKey != null && m_HadKey.ContainsKey(key) && m_HadKey[key])
                    PlayerPrefs.SetInt(key, m_SavedInts[key]);
                else
                    PlayerPrefs.DeleteKey(key);
            }

            foreach (var key in AnchorPrefKeysThatMustNotExist)
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        static Dictionary<string, string> CaptureKnownPrefs()
        {
            var values = new Dictionary<string, string>();
            foreach (var key in FloatPrefKeys)
                values[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetFloat(key).ToString("R") : "<missing>";
            foreach (var key in IntPrefKeys)
                values[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key).ToString() : "<missing>";
            return values;
        }
    }
}
