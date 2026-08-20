using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class ScrapSceneTransitionTests
{
    private TransitionFixture _fixture;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 0f;
        _fixture = new TransitionFixture();
    }

    [TearDown]
    public void TearDown()
    {
        _fixture.Dispose();
        Time.timeScale = 1f;
    }

    [Test]
    public void TryLoad_RejectsDuplicate_AndRoutesExactlyOnceAfterFullCover()
    {
        int routeCount = 0;
        bool wasFullyCoveredWhenRouted = false;
        SetRoute(destination =>
        {
            routeCount++;
            wasFullyCoveredWhenRouted =
                destination == SceneDestination.Play &&
                Vector2.Distance(_fixture.Warning.anchoredPosition, _fixture.WarningCoveredPosition) < 0.001f &&
                Vector2.Distance(_fixture.Plate.anchoredPosition, _fixture.PlateCoveredPosition) < 0.001f &&
                _fixture.Overlay.blocksRaycasts;
            return true;
        });

        Assert.That(_fixture.Transition.TryLoad(SceneDestination.Play), Is.True);
        Assert.That(_fixture.Transition.TryLoad(SceneDestination.WeaponSandbox), Is.False);

        Advance(0.299f);

        Assert.That(routeCount, Is.Zero, "The route must not run before every authored target reaches full cover.");

        Advance(0.001f);

        Assert.That(routeCount, Is.EqualTo(1));
        Assert.That(wasFullyCoveredWhenRouted, Is.True);

        Advance(0.5f);

        Assert.That(routeCount, Is.EqualTo(1), "Waiting for sceneLoaded must not invoke the route again.");
        Assert.That(_fixture.Transition.IsTransitioning, Is.True);
        Assert.That(_fixture.Overlay.blocksRaycasts, Is.True);
    }

    [Test]
    public void SceneLoadedNotification_RevealsOnlyAfterMinimumCoveredHold()
    {
        SetRoute(_ => true);
        Assert.That(_fixture.Transition.TryLoad(SceneDestination.Play), Is.True);

        Advance(0.3f);
        NotifySceneLoaded();
        Advance(0.049f);

        Assert.That(_fixture.Transition.IsTransitioning, Is.True);
        Assert.That(_fixture.Plate.anchoredPosition, Is.EqualTo(_fixture.PlateCoveredPosition).Using(Vector2Comparer));
        Assert.That(_fixture.Overlay.blocksRaycasts, Is.True);

        Advance(0.001f);
        Advance(0.15f);

        Assert.That(_fixture.Transition.IsTransitioning, Is.False);
        Assert.That(_fixture.Overlay.alpha, Is.Zero);
        Assert.That(_fixture.Overlay.blocksRaycasts, Is.False);
        Assert.That(_fixture.Warning.anchoredPosition, Is.EqualTo(_fixture.WarningOpenPosition).Using(Vector2Comparer));
        Assert.That(_fixture.Plate.anchoredPosition, Is.EqualTo(_fixture.PlateOpenPosition).Using(Vector2Comparer));
    }

    [Test]
    public void FailedRoute_RevealsCurrentSceneAndRestoresInteraction()
    {
        int routeCount = 0;
        SetRoute(_ =>
        {
            routeCount++;
            return false;
        });

        Assert.That(_fixture.Transition.TryLoad(SceneDestination.EnemiesTesting), Is.True);
        LogAssert.Expect(
            LogType.Error,
            "ScrapSceneTransition could not load 'enemiesTesting'. The current scene will be uncovered.");
        Advance(0.3f);

        Assert.That(routeCount, Is.EqualTo(1));
        Assert.That(_fixture.Transition.IsTransitioning, Is.True);
        Assert.That(_fixture.Overlay.blocksRaycasts, Is.True);

        Advance(0.15f);

        Assert.That(routeCount, Is.EqualTo(1));
        Assert.That(_fixture.Transition.IsTransitioning, Is.False);
        Assert.That(_fixture.Overlay.alpha, Is.Zero);
        Assert.That(_fixture.Overlay.blocksRaycasts, Is.False);
        Assert.That(_fixture.Plate.anchoredPosition, Is.EqualTo(_fixture.PlateOpenPosition).Using(Vector2Comparer));
    }

    [Test]
    public void Awake_UsesTheAuthoredHierarchyWithoutCreatingReplacementObjects()
    {
        Assert.That(ScrapSceneTransition.Instance, Is.SameAs(_fixture.Transition));
        Assert.That(_fixture.Root.transform.childCount, Is.EqualTo(2));
        Assert.That(_fixture.Root.GetComponentsInChildren<ScrapSceneTransition>(true), Has.Length.EqualTo(1));
    }

    [Test]
    public void AuthoredPrefab_InputBlockerIsTransparentAndStillReceivesRaycasts()
    {
        const string prefabPath = "Assets/Prefabs/UI/MainMenu/ScrapSceneTransition.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        Assert.That(prefab, Is.Not.Null, $"Expected authored transition prefab at '{prefabPath}'.");
        Image blocker = Array.Find(
            prefab.GetComponentsInChildren<Image>(true),
            image => image.name == "InputBlocker");

        Assert.That(blocker, Is.Not.Null, "The authored transition is missing InputBlocker.");
        Assert.That(blocker.color.a, Is.Zero.Within(0.0001f));
        Assert.That(blocker.raycastTarget, Is.True);
    }

    [Test]
    public void ReducedMotion_SnapsToCoveredPoseAndUsesShortOpaqueFades()
    {
        int routeCount = 0;
        bool fullyCoveredWhenRouted = false;
        SetInternalProvider("ReducedMotion", new Func<bool>(() => true));
        SetRoute(_ =>
        {
            routeCount++;
            fullyCoveredWhenRouted =
                Mathf.Approximately(_fixture.Overlay.alpha, 1f) &&
                Vector2.Distance(_fixture.Warning.anchoredPosition, _fixture.WarningCoveredPosition) < 0.001f &&
                Vector2.Distance(_fixture.Plate.anchoredPosition, _fixture.PlateCoveredPosition) < 0.001f;
            return true;
        });

        Assert.That(_fixture.Transition.TryLoad(SceneDestination.Play), Is.True);
        Assert.That(_fixture.Warning.anchoredPosition, Is.EqualTo(_fixture.WarningCoveredPosition).Using(Vector2Comparer));
        Assert.That(_fixture.Plate.anchoredPosition, Is.EqualTo(_fixture.PlateCoveredPosition).Using(Vector2Comparer));
        Assert.That(_fixture.Overlay.alpha, Is.Zero);
        Assert.That(_fixture.Overlay.blocksRaycasts, Is.True);

        Advance(0.03f);

        Assert.That(_fixture.Overlay.alpha, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(_fixture.Plate.anchoredPosition, Is.EqualTo(_fixture.PlateCoveredPosition).Using(Vector2Comparer));
        Assert.That(routeCount, Is.Zero);

        Advance(0.03f);

        Assert.That(routeCount, Is.EqualTo(1));
        Assert.That(fullyCoveredWhenRouted, Is.True);

        NotifySceneLoaded();
        Advance(0.05f);
        Advance(0.03f);

        Assert.That(_fixture.Overlay.alpha, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(_fixture.Plate.anchoredPosition, Is.EqualTo(_fixture.PlateCoveredPosition).Using(Vector2Comparer));

        Advance(0.03f);

        Assert.That(_fixture.Transition.IsTransitioning, Is.False);
        Assert.That(_fixture.Overlay.blocksRaycasts, Is.False);
        Assert.That(_fixture.Plate.anchoredPosition, Is.EqualTo(_fixture.PlateOpenPosition).Using(Vector2Comparer));
    }

    [Test]
    public void OneShotVolume_MultipliesAuthoredVolumeBySharedSfxVolume()
    {
        SetInternalProvider("SfxVolume", new Func<float>(() => 0.25f));

        float effectiveVolume = (float)InvokeInternalWithResult(
            "GetEffectiveOneShotVolumeForTesting",
            0.8f);

        Assert.That(effectiveVolume, Is.EqualTo(0.2f).Within(0.0001f));
    }

    private void SetRoute(Func<SceneDestination, bool> route)
    {
        SetInternalProvider("Route", route);
    }

    private void SetInternalProvider(string propertyName, object value)
    {
        PropertyInfo property = typeof(ScrapSceneTransition).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null);
        property.SetValue(_fixture.Transition, value);
    }

    private void Advance(float unscaledDeltaTime)
    {
        InvokeInternal("AdvanceForTesting", unscaledDeltaTime);
    }

    private void NotifySceneLoaded()
    {
        InvokeInternal("NotifySceneLoadedForTesting");
    }

    private void InvokeInternal(string methodName, params object[] arguments)
    {
        InvokeInternalWithResult(methodName, arguments);
    }

    private object InvokeInternalWithResult(string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(ScrapSceneTransition).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method.Invoke(_fixture.Transition, arguments);
    }

    private static readonly Vector2EqualityComparer Vector2Comparer = new(0.0001f);

    private sealed class TransitionFixture : IDisposable
    {
        public readonly GameObject Root;
        public readonly ScrapSceneTransition Transition;
        public readonly CanvasGroup Overlay;
        public readonly RectTransform Warning;
        public readonly RectTransform Plate;
        public readonly Vector2 WarningOpenPosition = new(-400f, 20f);
        public readonly Vector2 WarningCoveredPosition = new(0f, 20f);
        public readonly Vector2 PlateOpenPosition = new(-900f, -30f);
        public readonly Vector2 PlateCoveredPosition = new(0f, -30f);

        public TransitionFixture()
        {
            Root = new GameObject("AuthoredPersistentTransition", typeof(RectTransform), typeof(CanvasGroup));
            Root.SetActive(false);
            Overlay = Root.GetComponent<CanvasGroup>();

            Warning = CreateAuthoredTarget("WarningBlade", WarningOpenPosition);
            Plate = CreateAuthoredTarget("UpperJaw", PlateOpenPosition);

            Transition = Root.AddComponent<ScrapSceneTransition>();
            SetField("_overlay", Overlay);
            SetField(
                "_warningBlade",
                new ScrapSceneTransition.RectTransformTarget(Warning, WarningCoveredPosition, -2f));
            SetField(
                "_coverPlates",
                new[]
                {
                    new ScrapSceneTransition.RectTransformTarget(Plate, PlateCoveredPosition, 1f)
                });
            SetField("_warningBladeDuration", 0.1f);
            SetField("_coverDuration", 0.2f);
            SetField("_minimumCoveredHold", 0.05f);
            SetField("_revealDuration", 0.15f);
            SetField("_sceneLoadTimeout", 1f);
            SetField("_reducedMotionFadeDuration", 0.06f);
            SetField("_coverCurve", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            SetField("_revealCurve", AnimationCurve.Linear(0f, 0f, 1f, 1f));

            Root.SetActive(true);
            InvokeLifecycle("Awake");
            InvokeLifecycle("OnEnable");
        }

        public void Dispose()
        {
            if (Root == null)
                return;

            InvokeLifecycle("OnDisable");
            InvokeLifecycle("OnDestroy");
            UnityEngine.Object.DestroyImmediate(Root);
        }

        private RectTransform CreateAuthoredTarget(string name, Vector2 anchoredPosition)
        {
            GameObject target = new(name, typeof(RectTransform));
            RectTransform rectTransform = target.GetComponent<RectTransform>();
            rectTransform.SetParent(Root.transform, false);
            rectTransform.anchoredPosition = anchoredPosition;
            return rectTransform;
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(ScrapSceneTransition).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{name}'.");
            field.SetValue(Transition, value);
        }

        private void InvokeLifecycle(string methodName)
        {
            MethodInfo method = typeof(ScrapSceneTransition).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Expected lifecycle method '{methodName}'.");
            method.Invoke(Transition, null);
        }
    }

    private sealed class Vector2EqualityComparer : System.Collections.IComparer
    {
        private readonly float _tolerance;

        public Vector2EqualityComparer(float tolerance)
        {
            _tolerance = tolerance;
        }

        public int Compare(object x, object y)
        {
            Vector2 left = (Vector2)x;
            Vector2 right = (Vector2)y;
            return Vector2.Distance(left, right) <= _tolerance ? 0 : 1;
        }
    }
}
