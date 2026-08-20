using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class ObjectivesMenuUIPresenterTests
{
    private readonly List<UnityEngine.Object> _owned = new();
    private SaveManager _previousSaveManager;
    private SaveManager _saveManager;
    private SaveData _saveData;
    private string _savePath;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        _previousSaveManager = SaveManager.Instance;
        SetSaveManagerInstance(null);

        GameObject saveRoot = Own(new GameObject("TestSaveManager"));
        saveRoot.SetActive(false);
        _saveManager = saveRoot.AddComponent<SaveManager>();
        _saveData = new SaveData();
        _savePath = Path.Combine(Path.GetTempPath(), $"scrapwaves_objectives_{Guid.NewGuid():N}.json");
        SetPrivate(_saveManager, "_data", _saveData);
        SetPrivate(_saveManager, "_path", _savePath);
        SetPrivate(_saveManager, "_achievementCatalog", new List<AchievementDefinition>());
        SetSaveManagerInstance(_saveManager);
    }

    [TearDown]
    public void TearDown()
    {
        SetSaveManagerInstance(_previousSaveManager != null ? _previousSaveManager : null);

        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            if (_owned[i] != null)
                UnityEngine.Object.DestroyImmediate(_owned[i]);
        }
        _owned.Clear();

        if (!string.IsNullOrEmpty(_savePath) && File.Exists(_savePath))
            File.Delete(_savePath);
    }

    [Test]
    public void Show_BindsExactProgressDetailsScrapTabsAndUnlockStateFromAuthoredTemplates()
    {
        AchievementDefinition achievement = CreateAchievement("crusher", "CRUSHER", "Crush five enemies.", 5f, 25);
        _saveData.TotalEnemiesKilled = 3;
        SetPrivate(_saveManager, "_achievementCatalog", new List<AchievementDefinition> { achievement });

        PassiveItemData item = CreatePassive("shock-coil", "Shock Coil", 50);
        UnlockCatalog catalog = CreateCatalog(item);
        PresenterFixture fixture = CreatePresenter(catalog);

        fixture.Presenter.Show();

        Assert.That(fixture.Presenter.HasRequiredReferences, Is.True);
        Assert.That(fixture.Presenter.IsVisible, Is.True);
        Assert.That(fixture.Presenter.ActiveTab, Is.EqualTo(ObjectivesMenuTab.Objectives));
        Assert.That(fixture.ObjectivesTabRoot.activeSelf, Is.True);
        Assert.That(fixture.UnlocksTabRoot.activeSelf, Is.False);
        Assert.That(fixture.ScrapText.text, Is.EqualTo("SCRAP: 0"));
        Assert.That(fixture.ObjectivesContent.childCount, Is.EqualTo(1));

        ObjectiveRowView row = fixture.ObjectivesContent.GetChild(0).GetComponent<ObjectiveRowView>();
        Assert.That(row, Is.Not.Null);
        Assert.That(row.CurrentProgress, Is.EqualTo(3f));
        Assert.That(row.TargetProgress, Is.EqualTo(5f));
        Assert.That(fixture.ObjectiveProgressText.text, Is.EqualTo("3 / 5"));
        Assert.That(fixture.ObjectiveNameText.text, Is.EqualTo("CRUSHER"));
        Assert.That(fixture.ObjectiveDescriptionText.text, Is.EqualTo("Crush five enemies."));
        Assert.That(fixture.ObjectiveRewardText.text, Does.Contain("25 SCRAP"));
        Assert.That(fixture.ObjectivesEmpty.activeSelf, Is.False);
        Assert.That(fixture.ObjectivesUnavailable.activeSelf, Is.False);

        fixture.Presenter.ShowUnlocksTab();

        Assert.That(fixture.Presenter.ActiveTab, Is.EqualTo(ObjectivesMenuTab.Unlocks));
        Assert.That(fixture.ObjectivesTabRoot.activeSelf, Is.False);
        Assert.That(fixture.UnlocksTabRoot.activeSelf, Is.True);
        Assert.That(fixture.UnlocksContent.childCount, Is.EqualTo(1));
        UnlockCardView card = fixture.UnlocksContent.GetChild(0).GetComponent<UnlockCardView>();
        Assert.That(card.DisplayName, Is.EqualTo("Shock Coil"));
        Assert.That(card.ItemType, Is.EqualTo("PASSIVE"));
        Assert.That(card.State, Is.EqualTo(UnlockCardState.InsufficientScrap));
        Assert.That(fixture.UnlockNameText.text, Is.EqualTo("Shock Coil"));
        Assert.That(fixture.UnlockTypeText.text, Is.EqualTo("PASSIVE"));
        Assert.That(fixture.UnlockStatusText.text, Is.EqualTo("INSUFFICIENT SCRAP"));
        Assert.That(fixture.PurchaseButton.interactable, Is.False);
        Assert.That(fixture.PurchaseButtonLabel.text, Is.EqualTo("NEED 50 SCRAP"));
    }

    [Test]
    public void ScrapPurchase_ArmsThenBackCancelsAndSecondConfirmPurchasesExactlyOnce()
    {
        _saveData.Scrap = 75;
        PassiveItemData item = CreatePassive("shock-coil", "Shock Coil", 50);
        PresenterFixture fixture = CreatePresenter(CreateCatalog(item));
        fixture.Presenter.Show();
        fixture.Presenter.ShowUnlocksTab();

        fixture.PurchaseButton.onClick.Invoke();

        Assert.That(fixture.Presenter.IsPurchaseArmed, Is.True);
        Assert.That(_saveManager.Scrap, Is.EqualTo(75));
        Assert.That(fixture.PurchaseButtonLabel.text, Is.EqualTo("CONFIRM — 50 SCRAP"));

        fixture.Presenter.HandleBackRequested();

        Assert.That(fixture.Presenter.IsPurchaseArmed, Is.False);
        Assert.That(fixture.Presenter.IsVisible, Is.True, "The first Back cancels an armed purchase instead of closing the screen.");
        Assert.That(_saveManager.Scrap, Is.EqualTo(75));

        fixture.PurchaseButton.onClick.Invoke();
        fixture.PurchaseButton.onClick.Invoke();

        Assert.That(fixture.Presenter.IsPurchaseArmed, Is.False);
        Assert.That(_saveManager.Scrap, Is.EqualTo(25));
        Assert.That(_saveManager.IsUnlocked(item), Is.True);
        Assert.That(fixture.PurchaseButton.interactable, Is.False);
        Assert.That(fixture.PurchaseButtonLabel.text, Is.EqualTo("OWNED"));
        Assert.That(fixture.PurchaseFeedbackText.text, Is.EqualTo("PURCHASE COMPLETE"));
    }

    [Test]
    public void FocusingAnotherUnlockCard_CancelsArmedPurchaseWithoutSpendingScrap()
    {
        _saveData.Scrap = 100;
        PassiveItemData first = CreatePassive("first", "First", 40);
        PassiveItemData second = CreatePassive("second", "Second", 30);
        PresenterFixture fixture = CreatePresenter(CreateCatalog(first, second));
        fixture.Presenter.Show();
        fixture.Presenter.ShowUnlocksTab();
        fixture.PurchaseButton.onClick.Invoke();

        Assert.That(fixture.Presenter.IsPurchaseArmed, Is.True);
        Assert.That(fixture.Presenter.SelectedUnlockId, Is.EqualTo("first"));

        UnlockCardView secondCard = fixture.UnlocksContent.GetChild(1).GetComponent<UnlockCardView>();
        secondCard.OnSelect(null);

        Assert.That(fixture.Presenter.IsPurchaseArmed, Is.False);
        Assert.That(fixture.Presenter.SelectedUnlockId, Is.EqualTo("second"));
        Assert.That(_saveManager.Scrap, Is.EqualTo(100));
        Assert.That(_saveManager.IsUnlocked(first), Is.False);
        Assert.That(_saveManager.IsUnlocked(second), Is.False);
    }

    [Test]
    public void MissingObjectivePrefab_ShowsAuthoredUnavailableStateAndCreatesNoFallbackRows()
    {
        AchievementDefinition achievement = CreateAchievement("crusher", "CRUSHER", "Crush five enemies.", 5f, 0);
        SetPrivate(_saveManager, "_achievementCatalog", new List<AchievementDefinition> { achievement });
        PresenterFixture fixture = CreatePresenter(CreateCatalog(), includeObjectiveTemplate: false);
        LogAssert.Expect(LogType.Error, new Regex("missing authored references: .*_objectiveRowPrefab"));

        fixture.Presenter.Show();

        Assert.That(fixture.Presenter.HasRequiredReferences, Is.False);
        Assert.That(fixture.ObjectivesContent.childCount, Is.Zero);
        Assert.That(fixture.ObjectivesUnavailable.activeSelf, Is.True);
        Assert.That(fixture.ObjectivesEmpty.activeSelf, Is.False);
    }

    [Test]
    public void FocusingOffscreenObjectiveRow_ScrollsItInsideAuthoredViewport()
    {
        List<AchievementDefinition> achievements = new();
        for (int i = 0; i < 8; i++)
            achievements.Add(CreateAchievement($"objective-{i}", $"OBJECTIVE {i}", "Test objective.", 1f, 0));
        SetPrivate(_saveManager, "_achievementCatalog", achievements);

        PresenterFixture fixture = CreatePresenter(CreateCatalog());
        fixture.Presenter.Show();

        ObjectiveRowView lastRow = fixture.ObjectivesContent
            .GetChild(fixture.ObjectivesContent.childCount - 1)
            .GetComponent<ObjectiveRowView>();
        lastRow.OnSelect(null);

        AssertFullyVisible(fixture.ObjectivesScrollRect, lastRow.transform as RectTransform);
    }

    [Test]
    public void FocusingLastCardInSeventeenCardGrid_ScrollsItInsideAuthoredViewport()
    {
        PassiveItemData[] items = new PassiveItemData[17];
        for (int i = 0; i < items.Length; i++)
            items[i] = CreatePassive($"item-{i}", $"Item {i}", 10);

        PresenterFixture fixture = CreatePresenter(CreateCatalog(items));
        fixture.Presenter.Show();
        fixture.Presenter.ShowUnlocksTab();

        UnlockCardView lastCard = fixture.UnlocksContent
            .GetChild(fixture.UnlocksContent.childCount - 1)
            .GetComponent<UnlockCardView>();
        lastCard.OnSelect(null);

        Assert.That(fixture.UnlocksContent.childCount, Is.EqualTo(17));
        AssertFullyVisible(fixture.UnlocksScrollRect, lastCard.transform as RectTransform);
    }

    private PresenterFixture CreatePresenter(UnlockCatalog catalog, bool includeObjectiveTemplate = true)
    {
        GameObject host = Own(new GameObject("ObjectivesPresenterHost"));
        host.SetActive(false);
        ObjectivesMenuUI presenter = host.AddComponent<ObjectivesMenuUI>();

        GameObject screenRoot = Own(new GameObject("ObjectivesScreen"));
        GameObject objectivesTabRoot = CreateChild("ObjectivesTab", screenRoot.transform);
        GameObject unlocksTabRoot = CreateChild("UnlocksTab", screenRoot.transform);
        ScrollRect objectivesScrollRect = CreateScrollRect(
            "ObjectivesScroll",
            objectivesTabRoot.transform,
            false,
            out RectTransform objectivesContent);
        ScrollRect unlocksScrollRect = CreateScrollRect(
            "UnlocksScroll",
            unlocksTabRoot.transform,
            true,
            out RectTransform unlocksContent);

        Button backButton = CreateButton("Back", screenRoot.transform);
        Button objectivesTabButton = CreateButton("ObjectivesTabButton", screenRoot.transform);
        Button unlocksTabButton = CreateButton("UnlocksTabButton", screenRoot.transform);
        TextMeshProUGUI scrapText = CreateText("Scrap", screenRoot.transform);
        GameObject objectivesTabSelected = CreateChild("ObjectivesTabSelected", screenRoot.transform);
        GameObject unlocksTabSelected = CreateChild("UnlocksTabSelected", screenRoot.transform);

        GameObject objectivesEmpty = CreateChild("ObjectivesEmpty", objectivesTabRoot.transform);
        GameObject objectivesUnavailable = CreateChild("ObjectivesUnavailable", objectivesTabRoot.transform);
        GameObject objectiveDetailRoot = CreateChild("ObjectiveDetail", objectivesTabRoot.transform);
        GameObject objectiveDetailEmpty = CreateChild("ObjectiveDetailEmpty", objectivesTabRoot.transform);
        TextMeshProUGUI objectiveName = CreateText("ObjectiveName", objectiveDetailRoot.transform);
        TextMeshProUGUI objectiveDescription = CreateText("ObjectiveDescription", objectiveDetailRoot.transform);
        TextMeshProUGUI objectiveProgress = CreateText("ObjectiveProgress", objectiveDetailRoot.transform);
        Slider objectiveProgressBar = CreateSlider("ObjectiveProgressBar", objectiveDetailRoot.transform);
        TextMeshProUGUI objectiveCompletion = CreateText("ObjectiveCompletion", objectiveDetailRoot.transform);
        TextMeshProUGUI objectiveReward = CreateText("ObjectiveReward", objectiveDetailRoot.transform);

        GameObject unlocksEmpty = CreateChild("UnlocksEmpty", unlocksTabRoot.transform);
        GameObject unlocksUnavailable = CreateChild("UnlocksUnavailable", unlocksTabRoot.transform);
        GameObject unlockDetailRoot = CreateChild("UnlockDetail", unlocksTabRoot.transform);
        GameObject unlockDetailEmpty = CreateChild("UnlockDetailEmpty", unlocksTabRoot.transform);
        TextMeshProUGUI unlockName = CreateText("UnlockName", unlockDetailRoot.transform);
        TextMeshProUGUI unlockType = CreateText("UnlockType", unlockDetailRoot.transform);
        TextMeshProUGUI unlockStatus = CreateText("UnlockStatus", unlockDetailRoot.transform);
        TextMeshProUGUI unlockRequirement = CreateText("UnlockRequirement", unlockDetailRoot.transform);
        Button purchaseButton = CreateButton("Purchase", unlockDetailRoot.transform);
        TextMeshProUGUI purchaseButtonLabel = CreateText("PurchaseLabel", purchaseButton.transform);
        TextMeshProUGUI purchaseFeedback = CreateText("PurchaseFeedback", unlockDetailRoot.transform);

        ObjectiveRowView objectiveTemplate = includeObjectiveTemplate ? CreateObjectiveTemplate() : null;
        UnlockCardView unlockTemplate = CreateUnlockTemplate();

        SetPrivate(presenter, "_screenRoot", screenRoot);
        SetPrivate(presenter, "_backButton", backButton);
        SetPrivate(presenter, "_scrapText", scrapText);
        SetPrivate(presenter, "_objectivesTabButton", objectivesTabButton);
        SetPrivate(presenter, "_unlocksTabButton", unlocksTabButton);
        SetPrivate(presenter, "_objectivesTabSelectedState", objectivesTabSelected);
        SetPrivate(presenter, "_unlocksTabSelectedState", unlocksTabSelected);
        SetPrivate(presenter, "_objectivesTabRoot", objectivesTabRoot);
        SetPrivate(presenter, "_unlocksTabRoot", unlocksTabRoot);
        SetPrivate(presenter, "_objectivesScrollRect", objectivesScrollRect);
        SetPrivate(presenter, "_objectivesContent", objectivesContent);
        SetPrivate(presenter, "_objectiveRowPrefab", objectiveTemplate);
        SetPrivate(presenter, "_objectivesEmptyState", objectivesEmpty);
        SetPrivate(presenter, "_objectivesDataUnavailableState", objectivesUnavailable);
        SetPrivate(presenter, "_objectiveDetailRoot", objectiveDetailRoot);
        SetPrivate(presenter, "_objectiveDetailEmptyState", objectiveDetailEmpty);
        SetPrivate(presenter, "_objectiveNameText", objectiveName);
        SetPrivate(presenter, "_objectiveDescriptionText", objectiveDescription);
        SetPrivate(presenter, "_objectiveProgressText", objectiveProgress);
        SetPrivate(presenter, "_objectiveProgressBar", objectiveProgressBar);
        SetPrivate(presenter, "_objectiveCompletionText", objectiveCompletion);
        SetPrivate(presenter, "_objectiveRewardText", objectiveReward);
        SetPrivate(presenter, "_catalog", catalog);
        SetPrivate(presenter, "_unlocksScrollRect", unlocksScrollRect);
        SetPrivate(presenter, "_unlocksContent", unlocksContent);
        SetPrivate(presenter, "_unlockCardPrefab", unlockTemplate);
        SetPrivate(presenter, "_unlocksEmptyState", unlocksEmpty);
        SetPrivate(presenter, "_unlocksDataUnavailableState", unlocksUnavailable);
        SetPrivate(presenter, "_unlockDetailRoot", unlockDetailRoot);
        SetPrivate(presenter, "_unlockDetailEmptyState", unlockDetailEmpty);
        SetPrivate(presenter, "_unlockNameText", unlockName);
        SetPrivate(presenter, "_unlockTypeText", unlockType);
        SetPrivate(presenter, "_unlockStatusText", unlockStatus);
        SetPrivate(presenter, "_unlockRequirementText", unlockRequirement);
        SetPrivate(presenter, "_purchaseButton", purchaseButton);
        SetPrivate(presenter, "_purchaseButtonLabel", purchaseButtonLabel);
        SetPrivate(presenter, "_purchaseFeedbackText", purchaseFeedback);

        screenRoot.SetActive(false);
        return new PresenterFixture
        {
            Presenter = presenter,
            ScreenRoot = screenRoot,
            ObjectivesTabRoot = objectivesTabRoot,
            UnlocksTabRoot = unlocksTabRoot,
            ObjectivesScrollRect = objectivesScrollRect,
            UnlocksScrollRect = unlocksScrollRect,
            ObjectivesContent = objectivesContent,
            UnlocksContent = unlocksContent,
            ScrapText = scrapText,
            ObjectivesEmpty = objectivesEmpty,
            ObjectivesUnavailable = objectivesUnavailable,
            ObjectiveNameText = objectiveName,
            ObjectiveDescriptionText = objectiveDescription,
            ObjectiveProgressText = objectiveProgress,
            ObjectiveRewardText = objectiveReward,
            UnlockNameText = unlockName,
            UnlockTypeText = unlockType,
            UnlockStatusText = unlockStatus,
            PurchaseButton = purchaseButton,
            PurchaseButtonLabel = purchaseButtonLabel,
            PurchaseFeedbackText = purchaseFeedback
        };
    }

    private ObjectiveRowView CreateObjectiveTemplate()
    {
        GameObject root = Own(new GameObject("ObjectiveRowTemplate", typeof(RectTransform)));
        root.SetActive(false);
        Button button = root.AddComponent<Button>();
        ObjectiveRowView view = root.AddComponent<ObjectiveRowView>();
        SetPrivate(view, "_button", button);
        SetPrivate(view, "_nameText", CreateText("Name", root.transform));
        SetPrivate(view, "_progressText", CreateText("Progress", root.transform));
        SetPrivate(view, "_progressBar", CreateSlider("ProgressBar", root.transform));
        SetPrivate(view, "_completeStamp", CreateChild("Complete", root.transform));
        SetPrivate(view, "_selectedState", CreateChild("Selected", root.transform));
        return view;
    }

    private UnlockCardView CreateUnlockTemplate()
    {
        GameObject root = Own(new GameObject("UnlockCardTemplate", typeof(RectTransform)));
        root.SetActive(false);
        Button button = root.AddComponent<Button>();
        UnlockCardView view = root.AddComponent<UnlockCardView>();
        SetPrivate(view, "_button", button);
        SetPrivate(view, "_nameText", CreateText("Name", root.transform));
        SetPrivate(view, "_typeText", CreateText("Type", root.transform));
        SetPrivate(view, "_priceText", CreateText("Price", root.transform));
        SetPrivate(view, "_requirementText", CreateText("Requirement", root.transform));
        SetPrivate(view, "_statusText", CreateText("Status", root.transform));
        SetPrivate(view, "_ownedState", CreateChild("Owned", root.transform));
        SetPrivate(view, "_lockedState", CreateChild("Locked", root.transform));
        SetPrivate(view, "_purchasableState", CreateChild("Purchasable", root.transform));
        SetPrivate(view, "_selectedState", CreateChild("Selected", root.transform));
        return view;
    }

    private AchievementDefinition CreateAchievement(string id, string displayName, string description, float target, int reward)
    {
        AchievementDefinition achievement = Own(ScriptableObject.CreateInstance<AchievementDefinition>());
        achievement.name = id;
        SetPrivate(achievement, "_achievementId", id);
        SetPrivate(achievement, "_displayName", displayName);
        SetPrivate(achievement, "_description", description);
        SetPrivate(achievement, "_conditionType", AchievementConditionType.EnemiesKilledTotal);
        SetPrivate(achievement, "_targetValue", target);
        SetPrivate(achievement, "_scrapReward", reward);
        return achievement;
    }

    private PassiveItemData CreatePassive(string id, string displayName, int price)
    {
        PassiveItemData passive = Own(ScriptableObject.CreateInstance<PassiveItemData>());
        passive.name = id;
        SetPrivate(passive, "_displayName", displayName);
        SetPrivate(passive, "_unlockId", id);
        SetPrivate(passive, "_unlockedFromStart", false);
        SetPrivate(passive, "_requirement", new UnlockRequirement { ScrapPrice = price });
        return passive;
    }

    private UnlockCatalog CreateCatalog(params PassiveItemData[] items)
    {
        UnlockCatalog catalog = Own(ScriptableObject.CreateInstance<UnlockCatalog>());
        SetPrivate(catalog, "_weapons", new List<WeaponData>());
        SetPrivate(catalog, "_passiveItems", new List<PassiveItemData>(items));
        return catalog;
    }

    private static GameObject CreateChild(string name, Transform parent)
    {
        GameObject child = new(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static ScrollRect CreateScrollRect(
        string name,
        Transform parent,
        bool useGrid,
        out RectTransform content)
    {
        GameObject scrollObject = new(name, typeof(RectTransform), typeof(ScrollRect));
        scrollObject.transform.SetParent(parent, false);
        RectTransform scrollTransform = scrollObject.GetComponent<RectTransform>();
        scrollTransform.sizeDelta = new Vector2(520f, 220f);

        RectTransform viewport = CreateRect("Viewport", scrollTransform);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;

        content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);

        if (useGrid)
        {
            GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(160f, 90f);
            grid.spacing = new Vector2(10f, 10f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
        }
        else
        {
            VerticalLayoutGroup list = content.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 8f;
            list.childControlHeight = false;
            list.childForceExpandHeight = false;
        }

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.content = content;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        return scrollRect;
    }

    private static void AssertFullyVisible(ScrollRect scrollRect, RectTransform target)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        Canvas.ForceUpdateCanvases();

        Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scrollRect.viewport, target);
        Rect viewportRect = scrollRect.viewport.rect;
        Assert.That(targetBounds.max.y, Is.LessThanOrEqualTo(viewportRect.yMax + 0.5f));
        Assert.That(targetBounds.min.y, Is.GreaterThanOrEqualTo(viewportRect.yMin - 0.5f));
    }

    private static Button CreateButton(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.AddComponent<Button>();
    }

    private static Slider CreateSlider(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.AddComponent<Slider>();
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform), typeof(CanvasRenderer));
        child.transform.SetParent(parent, false);
        return child.AddComponent<TextMeshProUGUI>();
    }

    private T Own<T>(T value) where T : UnityEngine.Object
    {
        _owned.Add(value);
        return value;
    }

    private static void SetPrivate(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field '{fieldName}' on {instance.GetType().Name}.");
        field.SetValue(instance, value);
    }

    private static void SetSaveManagerInstance(SaveManager instance)
    {
        PropertyInfo property = typeof(SaveManager).GetProperty(nameof(SaveManager.Instance), BindingFlags.Static | BindingFlags.Public);
        MethodInfo setter = property?.GetSetMethod(true);
        Assert.That(setter, Is.Not.Null, "SaveManager.Instance private setter was not found.");
        setter.Invoke(null, new object[] { instance });
    }

    private sealed class PresenterFixture
    {
        public ObjectivesMenuUI Presenter;
        public GameObject ScreenRoot;
        public GameObject ObjectivesTabRoot;
        public GameObject UnlocksTabRoot;
        public ScrollRect ObjectivesScrollRect;
        public ScrollRect UnlocksScrollRect;
        public RectTransform ObjectivesContent;
        public RectTransform UnlocksContent;
        public TextMeshProUGUI ScrapText;
        public GameObject ObjectivesEmpty;
        public GameObject ObjectivesUnavailable;
        public TextMeshProUGUI ObjectiveNameText;
        public TextMeshProUGUI ObjectiveDescriptionText;
        public TextMeshProUGUI ObjectiveProgressText;
        public TextMeshProUGUI ObjectiveRewardText;
        public TextMeshProUGUI UnlockNameText;
        public TextMeshProUGUI UnlockTypeText;
        public TextMeshProUGUI UnlockStatusText;
        public Button PurchaseButton;
        public TextMeshProUGUI PurchaseButtonLabel;
        public TextMeshProUGUI PurchaseFeedbackText;
    }
}
