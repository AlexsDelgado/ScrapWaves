# Demo leaderboard

The title menu has two top-10 tables: **MOST KILLS** and **TIME SURVIVED**.
Online mode reads shared Unity Gaming Services (UGS) Leaderboards; an explicitly
labelled local mode remains available. The existing local score file is also updated
after qualifying results, independently of online submission.

This checkout is linked to **ScrapWaves** in organization **francourbini**, Cloud
project `5fc4808c-7c67-4fbb-8b44-cb5f40d4bad6`. Both production boards are configured.
On 2026-11-04, the game's UGS adapter authenticated two separate anonymous players,
submitted results and read each player's results from the other identity on both
boards. A standalone build and the physical three-PC check remain pending.

## Enable or disable

Open `Assets/Scenes/TitleScreen.unity`, select the root **Developer - Demo Leaderboard**,
and change **Enable Demo Leaderboard** in the Inspector. Save the scene to apply the
choice to builds. It is enabled for the demonstration.

- **ON:** Play and Play Tutorial ask for a name only until one is saved on this PC.
  Future launches reuse it. The main menu displays **Welcome back, [name]!** at
  the top right, with **CHANGE NAME** underneath. Saving a changed name returns
  to the menu without starting a run; Cancel keeps the previous name.
- **OFF:** both tables disappear, both Play routes skip the prompt, and those runs
  cannot write leaderboard scores. No new online operations are started. An already
  in-flight request may finish; it cannot be recalled. Existing local scores, pending
  uploads and remote scores remain intact.
- Changes in Play Mode apply to the current menu but are temporary. Each new
  scene launch uses its saved checkbox value.
- Direct gameplay scene entry and sandbox sessions remain unranked.

The same component contains the online settings:

| Inspector field | Default | Meaning |
| --- | --- | --- |
| Use Online Leaderboard | On | Show the shared boards. Off displays `LOCAL` / `This PC only`. |
| Environment Name | `production` | Must match the UGS environment on every demo PC. |
| Kills Leaderboard Id | `demo_most_kills` | Shared kills board. |
| Survival Leaderboard Id | `demo_time_survived` | Shared survival board; must differ from the kills ID. |
| Allow Online In Editor | Off | Explicit opt-in for manual live integration checks only. |

Save scene settings before building. Restart the game after changing the UGS
environment. In online mode, unavailable service data leaves the rows empty and
shows a `GLOBAL` status explaining the failure; local results are not substituted
into the global ranking. Menu refreshes are asynchronous and repeat 30 real-time
seconds after the previous request finishes while the view is active. They also retry
pending uploads. Navigation and gameplay do not wait for a refresh.

## Names and results

Names are required, limited to 16 characters, and rendered as plain text. Leading,
trailing and repeated spaces are normalized. On one PC, `Sam` and `SAM` reuse the
same anonymous Authentication profile and share one best result per table; different
aliases use separate profiles. Two people entering the same alias on that PC therefore
share its record. No account or password screen is shown to demo players.

The same alias on another PC is a separate player and can appear as another entry.
Aliases are stored as score metadata, not used as globally trusted identity. Duplicate
aliases visible within one fetched top ten are prefixed with a short player-ID hash,
for example `#A1B2 Sam`. This is a display aid, not proof of identity or a globally
unique name. Recognizing the same person across machines would require a separate
account/linking or player-code flow; this implementation does not provide one.

Both wins and losses qualify. Tutorial activity is excluded, and tutorial completion
resets the existing counters before normal gameplay. Time uses the existing scaled
gameplay clock: pausing, crafting, selection screens and the entrance cinematic do
not increase it. Victory records the time until escape, with longer time ranking higher.
Kills use the same enemy-elimination count displayed on the results screen.

Each table retains the player's best qualifying attempt independently. Kills rank by
kills, then whole elapsed survival seconds. Survival ranks by whole elapsed seconds,
then kills. Local full ties retain the earlier result. UGS full-tie ordering is unspecified;
the online board does not promise that the earliest run or participant wins a full tie.

Online scores encode both metrics into one exact integer: `primary * 67108864 +
secondary`. Each metric must be between 0 and **67,108,863** inclusive. The resulting
value is at most `2^52 - 1`, exactly representable by the SDK's `double` score. This
preserves the secondary comparison without client-side reordering. The UI decodes
the value back into kills or time. Do not seed these boards with raw kills, raw seconds
or another score format; entries also require the `scrapwaves-pair-v1` metadata marker.

Each completed attempt is captured once. Network retries may submit it again, so
both remote boards must use **Best Score**, not Total Score.

The remembered name is stored separately in `scrapwaves_demo_player.json`, beside
the local leaderboard file. It survives application restarts. Existing installations
choose their name once after this update; no name is guessed from old scores.
Changing it selects that alias for future runs, using the existing name-to-profile
mapping. Old scores and pending uploads retain their original alias and player
identity. Switching back to an old alias reuses its existing profile on this PC.
Invalid entries cannot be saved; a failed save leaves the prompt open for retry.

Retry retains the run's name and starts a new attempt. Returning to the menu reuses
the saved name on the next Play. Abandoning a run, quitting, a tutorial-only death, or a failed/cancelled
launch never submits a score.

## Shared boards and deployment

The project uses Unity **6000.3.13f1**. `Packages/manifest.json` pins
`com.unity.services.leaderboards` **2.3.4**; the resolved lock file currently contains
Authentication **3.6.1** and Services Core **1.16.0**. No multiplayer server, Relay,
Lobby or Cloud Save setup is required for this leaderboard implementation.

The operator linked this project and configured both **production** boards:
`demo_most_kills` and `demo_time_survived`. Both use **Highest to Lowest
(descending / DESC)** and **Best Score**, with no buckets, tiers or scheduled reset.
Successful live anonymous sign-ins confirmed that this authentication method is
enabled. No external identity provider or service-account secret is needed; do not
put dashboard/admin credentials in a build.

For deployment:

1. Keep the same Cloud Project ID, environment and board IDs in every build. If a
   separate test project or environment is selected later, provision matching boards
   with the configuration above and avoid mixing raw values with the encoded format.
2. Verify the scene's online settings and authored status label, then produce one
   standalone build. Copy that same build to all three demo PCs. Each host needs
   working internet access to UGS. Copy the application only: do not clone another
   machine's `PlayerPrefs`, Authentication session tokens or persistent save directory.
   Separate credentials are necessary to keep the installations' player identities
   separate.
3. As a manual integration check, complete a named run on each PC, return to the
   menu and confirm all machines converge on the same global rankings. Check two
   different aliases on one PC, a repeated alias on that PC and the same alias on
   another PC. Then disconnect one host, complete a run, restart it and reconnect;
   verify that its pending upload is retried and becomes visible globally. These are
   verification steps still to perform, not recorded test results.

Official references: [Leaderboards setup](https://docs.unity.com/en-us/leaderboards/get-started),
[update strategies](https://docs.unity.com/en-us/leaderboards/concepts/update-strategies),
[service environments](https://docs.unity.com/en-us/services/service-environments),
[Authentication profiles](https://docs.unity.com/en-us/authentication/profile-management).

## Pending uploads, local history and failures

When online mode is configured and permitted, every new qualifying result is offered
to a durable upload queue, including attempts outside the local top ten. The queue is
separate from the local score file and is saved before submission. Its filename is
`scrapwaves_online_pending_<scope-hash>.json`, beside the local leaderboard file;
the scope includes the Cloud project, environment and both board IDs. Changing any
of these selects another queue instead of publishing old pending results into another
competition.

The queue stores each run's alias profile, immutable run ID, authenticated player
binding and separate acknowledgements for the two boards. A result is removed only
after both board writes succeed and the removal is saved. Partial success resumes
with the remaining board after restart. Requests and profile changes are serialized
so a pending result is not sent under the next participant's identity.

If a bound player's cached credentials disappear or resolve to a different player,
that pending run is retained rather than reassigned; other players' uploads can still
proceed. Anonymous identity recovery across a wiped installation is not provided.
The queue does not contain the Authentication tokens themselves.

The queue holds at most **1,000 pending runs**. Queue overflow, invalid/out-of-range
metrics, an unavailable queue file or a failed disk write can prevent a new result
from being queued; status text and/or warnings report the failure. Existing queue
records are not evicted to make room. Invalid or unsupported queue files are preserved
and block writes. Local top-ten saving is attempted independently, but a failed online
enqueue does **not** guarantee that the attempt survives locally: it may not qualify
for either local top ten, and local disk writes can also fail.

Old local history is never bulk-imported. Runs completed while the Cloud project is
unlinked, online configuration is invalid, local mode is selected or Editor online
access is blocked are only offered to the local store; they are not queued for later
automatic upload. Once setup is valid, configured clients can queue new offline runs
and retry them when connectivity returns. A successful fetch can show global scores
while its status still reports pending uploads; a failed fetch clears both global
tables instead of showing a partial/local ranking.

## Clear local scores and test safely

Use **Tools > ScrapWaves > Demo Leaderboard > Clear Demo Scores** in the Editor.
The confirmation shows the exact file being reset. Only the local demo score file is
cleared; progression, unlocks, tutorial completion, pending uploads, Authentication
profiles and UGS scores are preserved. This command does not reset the global boards.
Changing remote scores or clearing pending uploads requires a separate deliberate
maintenance action.

The file is `scrapwaves_demo_leaderboard.json` under `Application.persistentDataPath`.
Editor validation honors `SCRAPWAVES_TEST_SAVE_DIRECTORY`, matching the existing save
system's isolation convention. Always set it to a disposable test directory when
automating menu or result flows. Do not clear the real demonstration file to run tests.

The online coordinator tests use a fake `IDemoLeaderboardRemote` and temporary queue
files. Live Editor access is disabled by default and remains blocked in batch mode or
whenever `SCRAPWAVES_TEST_SAVE_DIRECTORY` is set, even with the Inspector opt-in.
Use a deliberately selected test environment/boards for any manual live Editor check.

Invalid records are discarded on load. Invalid JSON is preserved and backed up before
the next save. Files from a future schema version are left untouched. Save failures
log a warning and allow the results flow to continue. These local-store recovery rules
are separate from the queue's preserve-and-block behavior described above.

Direct client submissions are suitable for a casual demo, not verified competitive
results. Both game metrics and aliases come from the client. Cloud Code validation
and Access Control restricting direct writes are possible later improvements, but
simply forwarding client-supplied numbers through Cloud Code does not prove genuine
gameplay. See [Unity's server access control guidance](https://docs.unity.com/en-us/cloud-code/server-access-control).

## Edit the UI

The controls are saved objects in `TitleScreen.unity`: **DemoLeaderboardRoot** under
the main menu's animated content and **PlayerNameEntry** under **SafeArea**. All
20 rows, input, labels and buttons can be edited directly in Unity. Runtime presenters
populate these controls and manage visibility; they never construct a replacement menu.

The authored **LeaderboardStatus** label displays global/local mode, connection state
and pending-upload information. If an older scene lacks it, use **Tools > ScrapWaves >
Demo Leaderboard > Add Online Status** outside Play Mode with scene work saved. This
additive command preserves the tables and menu layout and can be run again without
duplicating the label.

**Tools > ScrapWaves > Demo Leaderboard > Add Authored UI** is a one-time additive
authoring command. It validates an existing leaderboard without rebuilding it. The
retired title-menu rebuild command must not be used.

**Tools > ScrapWaves > Demo Leaderboard > Capture Menu Previews** renders copies of
the saved UI with clearly synthetic sample scores. It requires graphics and does not
read/write the leaderboard store or save scenes.

## Online integration verification, 2026-11-04

Unity 6000.3.13f1 resolved the SDK packages and compiled the implementation.
All **94 focused leaderboard tests passed**: the existing 53 checks plus 41 new
outbox, coordinator, score-codec and global/local UI cases. Network tests use fake
services; isolated save directories and the batch-mode guard prevent live UGS access.
The result artifact is `.utmp/online-leaderboard/tests-final.xml` (local, ignored by Git).

Unity rendered copies of the saved title UI with synthetic data. The new pending
status was visually checked at 1280x720 and the empty offline state at 1440x1080;
the status remains visible below the existing tables without covering menu controls.
Previews are under `.utmp/online-leaderboard/previews` (local, ignored by Git).

The subsequent authorized live check used the production project and the actual
`UgsDemoLeaderboardRemote` initialization, authentication, submission and read paths.
Two anonymous identities successfully read each other's records on both boards.
Switching back restored the first identity. A lower result retained its previous best;
a higher result replaced it. The kills board correctly used survival time to order
two players tied on kills. Initial reads found both boards empty.

The live probe used these deliberately small test records on **both** production
boards. After verification, all four score rows were deleted and both production
boards were confirmed empty on 2026-11-04:

| Alias | Player ID | Final kills | Final survival |
| --- | --- | --- | --- |
| `TEST SYNC A1104` | `YOh7xHvIPB1JoMh0UJ7vpLXeOTDC` | 2 | 2 seconds |
| `TEST SYNC B1104` | `P9WTh2gtr120Qc9AfjuyhZcykCNg` | 2 | 1 second |

The probe made eight score-update requests, resulting in two player rows per board.
Cleanup was performed separately after the probe. The temporary Editor-only helper
replaced the SDK's credential cache with a memory cache before sign-in, then signed
out and cleared it.
Checks before and after the run found no persisted Authentication keys for the tested
profiles. The helper and driver were removed from `Assets` after the check; normal
game Authentication behavior and the default Editor online guard are unchanged.

Sanitized evidence is `.utmp/online-leaderboard/live-verification.json` (local, ignored
by Git). This live check used separate identities within one Unity process on one PC;
it did not complete gameplay or exercise the durable queue against a real disconnect.
The end-to-end standalone and physical three-PC checks described above remain pending.
No additional Unity Services configuration blocker was found for these operations.

## Historical verification recorded 2026-10-31

The following record predates the online integration and describes the original local
feature only. It does not establish live UGS connectivity, online recovery behavior or
the appearance of the new status label. No additional pass counts are asserted here.

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
