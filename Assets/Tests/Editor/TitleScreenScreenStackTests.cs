using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class TitleScreenScreenStackTests
{
    private StackFixture _fixture;

    [SetUp]
    public void SetUp()
    {
        _fixture = new StackFixture();
    }

    [TearDown]
    public void TearDown()
    {
        _fixture?.Dispose();
    }

    [UnityTest]
    public IEnumerator OpenSettings_BlocksInvisibleControlsUntilAnimationCompletes()
    {
        int activationCount = 0;
        _fixture.InitialFocus.onClick.AddListener(() => activationCount++);
        _fixture.EventSystem.SetSelectedGameObject(_fixture.ReturnFocus.gameObject);

        Assert.That(_fixture.Stack.OpenSettings(_fixture.ReturnFocus.gameObject), Is.True);

        Assert.That(_fixture.Stack.IsInputLocked, Is.True);
        Assert.That(_fixture.Screen.activeSelf, Is.True);
        Assert.That(_fixture.Group.interactable, Is.False);
        Assert.That(_fixture.Group.blocksRaycasts, Is.False);
        Assert.That(_fixture.EventSystem.currentSelectedGameObject, Is.Not.SameAs(_fixture.InitialFocus.gameObject));

        ExecuteEvents.Execute(
            _fixture.InitialFocus.gameObject,
            new BaseEventData(_fixture.EventSystem),
            ExecuteEvents.submitHandler);
        Assert.That(activationCount, Is.Zero, "An invisible focused control must reject Submit during the open animation.");

        // An unrelated external unlock notification must not override the local transition lock.
        _fixture.Stack.SetInputLocked(false);
        Assert.That(_fixture.Group.interactable, Is.False);
        Assert.That(_fixture.Group.blocksRaycasts, Is.False);

        bool observedAnimationFrame = false;
        for (int frame = 0; frame < 120 && _fixture.Stack.IsInputLocked; frame++)
        {
            observedAnimationFrame = true;
            Assert.That(_fixture.Group.interactable, Is.False, $"Interaction leaked on open-animation frame {frame}.");
            Assert.That(_fixture.Group.blocksRaycasts, Is.False, $"Raycasts leaked on open-animation frame {frame}.");
            Assert.That(_fixture.EventSystem.currentSelectedGameObject, Is.Not.SameAs(_fixture.InitialFocus.gameObject));
            yield return null;
        }

        Assert.That(observedAnimationFrame, Is.True);
        Assert.That(_fixture.Stack.IsInputLocked, Is.False, "The authored open animation did not complete in the allotted frames.");
        Assert.That(_fixture.Group.alpha, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(_fixture.Group.interactable, Is.True);
        Assert.That(_fixture.Group.blocksRaycasts, Is.True);

        _fixture.EventSystem.SetSelectedGameObject(_fixture.InitialFocus.gameObject);
        bool selectedControlHandledCancel = ExecuteEvents.Execute(
            _fixture.EventSystem.currentSelectedGameObject,
            new BaseEventData(_fixture.EventSystem),
            ExecuteEvents.cancelHandler);
        Assert.That(
            selectedControlHandledCancel,
            Is.False,
            "InputSystemUIInputModule dispatches Cancel only to the selected control, which is why the authored action route is required.");

        _fixture.TriggerCancelAction();

        Assert.That(_fixture.Stack.IsInputLocked, Is.True, "The shared UI/Cancel action must begin the local close transition.");
        Assert.That(_fixture.Group.interactable, Is.False);
        Assert.That(_fixture.Group.blocksRaycasts, Is.False);
    }

    [UnityTest]
    public IEnumerator CancelAction_OnSelectedObjectiveControl_CancelsArmedPurchaseBeforeClose()
    {
        _fixture.ArmObjectivePurchase();
        Assert.That(_fixture.Stack.OpenObjectives(_fixture.ReturnFocus.gameObject), Is.True);

        for (int frame = 0; frame < 120 && _fixture.Stack.IsInputLocked; frame++)
            yield return null;

        Assert.That(_fixture.Stack.IsInputLocked, Is.False);
        Assert.That(_fixture.ObjectivesPresenter.IsPurchaseArmed, Is.True);
        _fixture.EventSystem.SetSelectedGameObject(_fixture.ObjectiveInitialFocus.gameObject);
        Assert.That(
            ExecuteEvents.Execute(
                _fixture.EventSystem.currentSelectedGameObject,
                new BaseEventData(_fixture.EventSystem),
                ExecuteEvents.cancelHandler),
            Is.False);

        _fixture.TriggerCancelAction();

        Assert.That(_fixture.ObjectivesPresenter.IsPurchaseArmed, Is.False);
        Assert.That(_fixture.Stack.CurrentState, Is.EqualTo(TitleScreenLocalState.Objectives));
        Assert.That(_fixture.Stack.IsInputLocked, Is.False, "The first Cancel must disarm the purchase without closing Objectives.");
        Assert.That(_fixture.ObjectivesGroup.interactable, Is.True);
    }

    [Test]
    public void MainMenuIntro_RejectsSubmitUntilFirstEntryIsReadable()
    {
        using PresentationFixture fixture = new();
        int activationCount = 0;
        fixture.Button.onClick.AddListener(() => activationCount++);

        fixture.Presentation.PlayIntro();

        Assert.That(fixture.VisualGroup.alpha, Is.Zero);
        Assert.That(fixture.MenuGroup.interactable, Is.False);
        Assert.That(fixture.MenuGroup.blocksRaycasts, Is.False);
        ExecuteEvents.Execute(
            fixture.Button.gameObject,
            new BaseEventData(fixture.EventSystem),
            ExecuteEvents.submitHandler);
        Assert.That(activationCount, Is.Zero);

        fixture.VisualGroup.alpha = 0.5f;
        InvokePrivate(fixture.Presentation, "TickIntro");

        Assert.That(fixture.MenuGroup.interactable, Is.True);
        Assert.That(fixture.MenuGroup.blocksRaycasts, Is.True);
        Assert.That(fixture.Button.IsInteractable(), Is.True);
    }

    [Test]
    public void MainMenuUndim_DoesNotCreateAnInteractiveDimmedFrame()
    {
        using PresentationFixture fixture = new();
        fixture.Presentation.CompleteIntroImmediately();
        fixture.Presentation.SetMainMenuDimmed(true, true);

        Assert.That(fixture.MenuGroup.alpha, Is.LessThan(1f));
        Assert.That(fixture.MenuGroup.interactable, Is.False);

        fixture.Presentation.SetMainMenuDimmed(false, false);

        Assert.That(fixture.MenuGroup.alpha, Is.LessThan(1f));
        Assert.That(fixture.MenuGroup.interactable, Is.False);
        Assert.That(fixture.MenuGroup.blocksRaycasts, Is.False);

        fixture.Presentation.SetMainMenuDimmed(false, true);
        Assert.That(fixture.MenuGroup.alpha, Is.EqualTo(1f));
        Assert.That(fixture.MenuGroup.interactable, Is.True);
        Assert.That(fixture.MenuGroup.blocksRaycasts, Is.True);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Expected method '{methodName}' on {target.GetType().Name}.");
        method.Invoke(target, null);
    }

    private sealed class StackFixture : IDisposable
    {
        private readonly GameObject _eventSystemObject;
        private readonly GameObject _root;

        public readonly TitleScreenScreenStack Stack;
        public readonly GameObject Screen;
        public readonly CanvasGroup Group;
        public readonly Button InitialFocus;
        public readonly Button ReturnFocus;
        public readonly EventSystem EventSystem;
        public readonly InputAction CancelAction;
        public readonly ObjectivesMenuUI ObjectivesPresenter;
        public readonly CanvasGroup ObjectivesGroup;
        public readonly Button ObjectiveInitialFocus;

        private readonly InputActionAsset _inputActions;
        private readonly InputActionReference _cancelReference;
        private readonly Keyboard _keyboard;
        private readonly PassiveItemData _armedItem;
        private readonly InputSettings.BackgroundBehavior _previousBackgroundBehavior;
        private readonly bool _previousRunPlayerUpdatesInEditMode;

        private const string RunPlayerUpdatesInEditModeFeature = "RUN_PLAYER_UPDATES_IN_EDIT_MODE";

        public StackFixture()
        {
            _eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            EventSystem = _eventSystemObject.GetComponent<EventSystem>();

            _inputActions = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap ui = _inputActions.AddActionMap("UI");
            CancelAction = ui.AddAction("Cancel", InputActionType.Button);
            CancelAction.AddBinding("<Keyboard>/escape");
            _cancelReference = InputActionReference.Create(CancelAction);
            _previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            _previousRunPlayerUpdatesInEditMode = IsInputFeatureEnabled(RunPlayerUpdatesInEditModeFeature);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.SetInternalFeatureFlag(RunPlayerUpdatesInEditModeFeature, true);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _keyboard.MakeCurrent();
            _inputActions.devices = new InputDevice[] { _keyboard };

            _root = new GameObject("TitleScreenStackFixture", typeof(RectTransform));
            _root.SetActive(false);
            Stack = _root.AddComponent<TitleScreenScreenStack>();

            Screen = new GameObject("SettingsScreen", typeof(RectTransform), typeof(CanvasGroup));
            Screen.transform.SetParent(_root.transform, false);
            Group = Screen.GetComponent<CanvasGroup>();

            GameObject objectivesScreen = new("ObjectivesScreen", typeof(RectTransform), typeof(CanvasGroup));
            objectivesScreen.transform.SetParent(_root.transform, false);
            ObjectivesGroup = objectivesScreen.GetComponent<CanvasGroup>();
            ObjectivesPresenter = objectivesScreen.AddComponent<ObjectivesMenuUI>();
            ObjectiveInitialFocus = CreateButton("ObjectiveInitialFocus", objectivesScreen.transform);

            InitialFocus = CreateButton("InitialFocus", Screen.transform);
            ReturnFocus = CreateButton("ReturnFocus", _root.transform);

            object objectivesBinding = CreateBinding(
                TitleScreenLocalState.Objectives,
                ObjectivesGroup,
                objectivesScreen.transform as RectTransform,
                ObjectiveInitialFocus);
            object settingsBinding = CreateBinding(
                TitleScreenLocalState.Settings,
                Group,
                Screen.transform as RectTransform,
                InitialFocus);
            SetField(Stack, "_objectives", objectivesBinding);
            SetField(Stack, "_settings", settingsBinding);
            SetField(Stack, "_objectivesPresenter", ObjectivesPresenter);
            SetField(Stack, "_cancelAction", _cancelReference);
            SetField(ObjectivesPresenter, typeof(ObjectivesMenuUI), "_screenStack", Stack);
            // EditMode UnityTests do not advance Time.unscaledDeltaTime after every yielded
            // editor frame. A one-tick duration still exercises the real coroutine's locked
            // frame and lets it complete on its next MoveNext.
            SetField(Stack, "_openDuration", 0.01f);
            SetField(Stack, "_curve", AnimationCurve.Linear(0f, 0f, 1f, 1f));

            _root.SetActive(true);
            InvokeLifecycle(Stack, "Awake");
            InvokeLifecycle(Stack, "OnEnable");
            CancelAction.Enable();

            _armedItem = ScriptableObject.CreateInstance<PassiveItemData>();
        }

        public void ArmObjectivePurchase()
        {
            SetField(ObjectivesPresenter, typeof(ObjectivesMenuUI), "_armedPurchase", _armedItem);
            SetField(ObjectivesPresenter, typeof(ObjectivesMenuUI), "_isVisible", true);
        }

        public void TriggerCancelAction()
        {
            Assert.That(_cancelReference.action, Is.SameAs(CancelAction));
            Assert.That(CancelAction.enabled, Is.True);
            Assert.That(_keyboard.enabled, Is.True);
            Assert.That(CancelAction.controls, Does.Contain(_keyboard.escapeKey));
            bool performed = false;
            CancelAction.performed += MarkPerformed;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            Assert.That(_keyboard.escapeKey.isPressed, Is.True, "The synthetic keyboard did not receive Escape state.");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
            CancelAction.performed -= MarkPerformed;
            Assert.That(performed, Is.True, "The synthetic Escape press did not perform the authored Cancel action.");

            void MarkPerformed(InputAction.CallbackContext _) => performed = true;
        }

        private static bool IsInputFeatureEnabled(string featureName)
        {
            MethodInfo isEnabled = typeof(InputSettings).GetMethod(
                "IsFeatureEnabled",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(isEnabled, Is.Not.Null);
            return (bool)isEnabled.Invoke(InputSystem.settings, new object[] { featureName });
        }

        public void Dispose()
        {
            InvokeLifecycle(Stack, "OnDisable");
            CancelAction.Disable();
            if (_keyboard != null && _keyboard.added)
                InputSystem.RemoveDevice(_keyboard);
            InputSystem.settings.SetInternalFeatureFlag(
                RunPlayerUpdatesInEditModeFeature,
                _previousRunPlayerUpdatesInEditMode);
            InputSystem.settings.backgroundBehavior = _previousBackgroundBehavior;
            if (_cancelReference != null)
                UnityEngine.Object.DestroyImmediate(_cancelReference);
            if (_inputActions != null)
                UnityEngine.Object.DestroyImmediate(_inputActions);
            if (_armedItem != null)
                UnityEngine.Object.DestroyImmediate(_armedItem);
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
            if (_eventSystemObject != null)
                UnityEngine.Object.DestroyImmediate(_eventSystemObject);
        }

        private static Button CreateButton(string name, Transform parent)
        {
            GameObject buttonObject = new(name, typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.GetComponent<Button>();
        }

        private static object CreateBinding(
            TitleScreenLocalState state,
            CanvasGroup canvasGroup,
            RectTransform animationRoot,
            Selectable initialFocus)
        {
            Type bindingType = typeof(TitleScreenScreenStack).GetNestedType(
                "LocalScreenBinding",
                BindingFlags.NonPublic);
            Assert.That(bindingType, Is.Not.Null);
            object binding = Activator.CreateInstance(bindingType, true);
            SetField(binding, bindingType, "_state", state);
            SetField(binding, bindingType, "_canvasGroup", canvasGroup);
            SetField(binding, bindingType, "_animationRoot", animationRoot);
            SetField(binding, bindingType, "_initialFocus", initialFocus);
            return binding;
        }

        private static void SetField(TitleScreenScreenStack stack, string name, object value)
        {
            SetField(stack, typeof(TitleScreenScreenStack), name, value);
        }

        private static void SetField(object target, Type targetType, string name, object value)
        {
            FieldInfo field = targetType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{name}' on {targetType.Name}.");
            field.SetValue(target, value);
        }

        private static void InvokeLifecycle(TitleScreenScreenStack stack, string name)
        {
            MethodInfo method = typeof(TitleScreenScreenStack).GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Expected lifecycle method '{name}'.");
            method.Invoke(stack, null);
        }
    }

    private sealed class PresentationFixture : IDisposable
    {
        private readonly GameObject _root;
        private readonly GameObject _eventSystemObject;

        public readonly MainMenuPresentationController Presentation;
        public readonly CanvasGroup MenuGroup;
        public readonly CanvasGroup VisualGroup;
        public readonly MainMenuItemView Item;
        public readonly Button Button;
        public readonly EventSystem EventSystem;

        public PresentationFixture()
        {
            _eventSystemObject = new GameObject("PresentationEventSystem", typeof(EventSystem));
            EventSystem = _eventSystemObject.GetComponent<EventSystem>();

            _root = new GameObject("PresentationFixture", typeof(RectTransform), typeof(CanvasGroup));
            MenuGroup = _root.GetComponent<CanvasGroup>();
            Presentation = _root.AddComponent<MainMenuPresentationController>();

            GameObject itemObject = new("PLAY", typeof(RectTransform), typeof(Button), typeof(MainMenuItemView));
            itemObject.transform.SetParent(_root.transform, false);
            Button = itemObject.GetComponent<Button>();
            Item = itemObject.GetComponent<MainMenuItemView>();

            GameObject visualObject = new("VisualRoot", typeof(RectTransform), typeof(CanvasGroup));
            visualObject.transform.SetParent(itemObject.transform, false);
            RectTransform visualRoot = visualObject.GetComponent<RectTransform>();
            VisualGroup = visualObject.GetComponent<CanvasGroup>();

            GameObject plateObject = new("Plate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            plateObject.transform.SetParent(visualObject.transform, false);
            Image plate = plateObject.GetComponent<Image>();

            GameObject labelObject = new("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(visualObject.transform, false);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "PLAY";

            SetField(Item, "_button", Button);
            SetField(Item, "_visualRoot", visualRoot);
            SetField(Item, "_visualCanvasGroup", VisualGroup);
            SetField(Item, "_plate", plate);
            SetField(Item, "_label", label);

            SetField(Presentation, "_mainMenuRoot", _root.transform as RectTransform);
            SetField(Presentation, "_mainMenuCanvasGroup", MenuGroup);
            SetField(Presentation, "_orderedItems", new[] { Item });
            SetField(Presentation, "_allowNavigationDuringIntroTail", true);

            InvokePrivate(Item, "Awake");
            InvokePrivate(Presentation, "Awake");
        }

        public void Dispose()
        {
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
            if (_eventSystemObject != null)
                UnityEngine.Object.DestroyImmediate(_eventSystemObject);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected field '{name}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
