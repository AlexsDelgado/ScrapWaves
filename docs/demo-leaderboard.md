# Demo leaderboard

The title menu has two local top-10 tables: **MOST KILLS** and **TIME SURVIVED**.
Scores belong to this computer and persist between application launches.

## Enable or disable

Open `Assets/Scenes/TitleScreen.unity`, select the root **Developer - Demo Leaderboard**,
and change **Enable Demo Leaderboard** in the Inspector. Save the scene to apply the
choice to builds. It is enabled for the demonstration.

- **ON:** Play and Play Tutorial ask for a name before starting.
- **OFF:** both tables disappear, both Play routes skip the prompt, and those runs
  cannot write leaderboard scores. Existing scores remain intact.
- Changes in Play Mode apply to the current menu but are temporary. Each new
  scene launch uses its saved checkbox value.
- Direct gameplay scene entry and sandbox sessions remain unranked.

## Names and results

Names are required, limited to 16 characters, and rendered as plain text. Leading,
trailing and repeated spaces are normalized. Case differences identify the same
player: `Sam` and `SAM` share one record per table. Two people using the same name
therefore share that leaderboard identity.

Both wins and losses qualify. Tutorial activity is excluded, and tutorial completion
resets the existing counters before normal gameplay. Time uses the existing scaled
gameplay clock: pausing, crafting, selection screens and the entrance cinematic do
not increase it. Victory records the time until escape, with longer time ranking higher.
Kills use the same enemy-elimination count displayed on the results screen.

Each table retains the player's best qualifying attempt independently. Kills rank by
kills, then survival time. Survival ranks by whole elapsed seconds, then kills. Equal
scores retain the earlier result. Each completed attempt is submitted once.

Retry retains the name and starts a new attempt. Returning to the menu asks again
on the next Play. Abandoning a run, quitting, a tutorial-only death, or a failed/cancelled
launch never submits a score.

## Clear scores and test safely

Use **Tools > ScrapWaves > Demo Leaderboard > Clear Demo Scores** in the Editor.
The confirmation shows the exact file being reset. Only demo scores are cleared;
progression, unlocks and tutorial completion are preserved.

The file is `scrapwaves_demo_leaderboard.json` under `Application.persistentDataPath`.
Editor validation honors `SCRAPWAVES_TEST_SAVE_DIRECTORY`, matching the existing save
system's isolation convention. Always set it to a disposable test directory when
automating menu or result flows. Do not clear the real demonstration file to run tests.

Invalid records are discarded on load. Invalid JSON is preserved and backed up before
the next save. Files from a future schema version are left untouched. Save failures
log a warning and allow the results flow to continue.

## Edit the UI

The controls are saved objects in `TitleScreen.unity`: **DemoLeaderboardRoot** under
the main menu's animated content and **PlayerNameEntry** under **SafeArea**. All
20 rows, input, labels and buttons can be edited directly in Unity. Runtime presenters
populate these controls and manage visibility; they never construct a replacement menu.

**Tools > ScrapWaves > Demo Leaderboard > Add Authored UI** is a one-time additive
authoring command. It validates an existing leaderboard without rebuilding it. The
retired title-menu rebuild command must not be used.

**Tools > ScrapWaves > Demo Leaderboard > Capture Menu Previews** renders copies of
the saved UI with clearly synthetic sample scores. It requires graphics and does not
read/write the leaderboard store or save scenes.

## Verification recorded 2026-10-31

Unity 6000.3.13f1 compiled and authored the feature. The latest results across targeted
runs pass 90 distinct checks, including all 53 leaderboard checks and the existing
fresh-save/tutorial replay smoke flow. The name-entry tests run in Play Mode and
exercise real button callbacks, focus, cancellation and animations; their scene-load
delegate is replaced to check duplicate and failed launches deterministically.

Unity renders checked the saved UI at 1920x1080, 1280x720, 1600x1000 and 1440x1080.
The boards stay inside the viewport and clear the existing logo and menu controls.
Narrow layouts abbreviate long names with ellipses. Preview scores are synthetic.

No standalone build or physical keyboard/gamepad click-through was performed.
The broader title validator retains its two pre-existing `UNLOCKS`/`OBJECTIVES`
label expectations; the designer's current `UNLOCKS` label was preserved.
