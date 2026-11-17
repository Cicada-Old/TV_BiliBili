using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TV_BiliBili
{
    // 正式 mod「店铺小电视：也许是B站？」— 阶段 C 冻结方案 v1 / 阶段 D 实现。
    // 形态（C §4.2）：播放器是原版 AdvCalendarUIManager.UIPanel 的第 4 个标签页；
    //   - 克隆原版 CalendarButton 进 TopToggleGroup，由 HorizontalLayoutGroup 排版；
    //   - 复用 OnAnySectionButtonClicked() 隐藏三个原版 section；
    //   - 三个原版按钮只追加 HidePlayerTab 监听，原回调不动；
    //   - 标题栏 / 关闭按钮由日历面板自带。
    // 生命周期唯一口径 = C §4.5 三档（用户 2026-11-14 21:26 决定，2026-11-15 06:35 冻结）：
    //   ① 游玩期切换（切标签 / 关日历）：继续播放，RT / URL / 进度 / 状态 / 外部投屏全部保留；
    //   ② 游戏内暂停（日结 / 离店 / ESC 菜单 / 选项浮层）：Pause，返回自动续播，手动暂停不被覆盖；
    //   ③ 离开存档（场景 EmporiumMenu）：Stop + 释放 RT + 恢复店铺 + 销毁跨场景对象，下次进店重建。
    // 【P-02】阶段 B 探针源码头部「切走即停播释放 RT」的 A §12.3 旧表述已作废并删除，
    //   不得再作为实现依据；本文件以上述三档状态机为准。
    // 基线：_证据/阶段B/probe/ProbeMod.cs（md5 df5963608006d18b21b190ee3868a8db）。
    public class TvBiliBiliMod : IMod
    {
        private const string ModId = "TV_BiliBili";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private const string Referer = "https://www.bilibili.com";

        // 运行日志（仅文本，写入本 mod 自己的 ModData；不写视频文件、不写玩家存档）。
        // V-01～V-12 的证据判据依赖下列日志行，故保留与探针一致的 Add/Flush 机制。
        private readonly StringBuilder report = new StringBuilder();
        private ModLog log;

        private VideoPlayer player;
        private AudioSource audioSource;
        private RenderTexture rt;
        private GameObject playerGo;
        // A 12: player content lives INSIDE the original calendar UIPanel.
        private GameObject panelRoot;          // DspPlayerTabContent (4th tab body)
        private GameObject playerTabButton;    // cloned stock CalendarButton
        private bool playerTabActive;
        private bool resumeAfterLifecycle;   // paused by day-end / leaving store
        private bool leftSave;               // cleared only when leaving the save
        // User 21:39: pause when the ESC menu (or its Options sub-panel) opens,
        // auto-resume when fully closed. The game has NO ModHook event for the
        // pause menu, so we fire the signal via Harmony patches on
        // EscapeUIManager.Open/CloseEscapeMenu and GameOptionUIManager.OpenUI /
        // OnWindowClosed. A depth counter keeps the nested menu+options case
        // from falsely resuming while a blocking overlay is still up.
        private static TvBiliBiliMod _instance;   // reachable from static Harmony postfixes
        private Harmony harmony;
        // User 21:50 fix: the ESC menu and its Options sub-panel are two separate
        // overlays. Clicking Options runs CloseEscapeMenu THEN OpenUI in the same
        // frame, so a naive depth counter resumed for one frame. We track each
        // overlay independently and DEFER the resume by a short slice so a
        // same-frame re-open cancels it instead of playing a one-frame blip.
        private bool escMenuOpen;
        private bool optMenuOpen;
        private bool menuPaused;
        private Coroutine pauseResumeCoroutine;
        private bool tabHooksAdded;
        private bool tabGeometrySaved;
        private Color tabTextColor = Color.white;   // sampled from the stock tab button
        private TextAnchor origTabAlignment;
        private Vector2 origTabSize;
        private Vector2 origTabPos;
        private AdvCalendarUIManager calendar;
        private CalendarTabWatcher calendarWatcher;
        private RawImage screen;
        private Slider progressSlider;
        private Slider volumeSlider;
        private TMP_InputField urlInput;
        private TextMeshProUGUI statusLabel;
        private TextMeshProUGUI timeLabel;
        private TextMeshProUGUI playPauseText;  // merged 播放 / 暂停 / 继续
        private string inputUrlText = "";
        // Persisted across panel close/reopen (user feedback 20:00 #2).
        private string lastUrlText = "";
        private string lastPlayedUrlText = "";   // URL currently loaded in VideoPlayer
        private string lastStatusText = "就绪";
        private string lastTimeText = "00:00 / 00:00";
        private bool progressLoopRunning;
        private bool layoutVerifyRunning;
        private readonly List<KeyValuePair<string, RectTransform>> placed = new List<KeyValuePair<string, RectTransform>>();

        // External in-store calendar display (the ORIGINAL version's video
        // surface, user clarification 20:02). Same RenderTexture, warped onto
        // the tilted yellow screen quad; dayTMP is hidden while playing.
        private GameObject externalOverlayGo;
        private WarpedTextureGraphic externalWarp;
        private TextMeshProUGUI dayTmp;
        private bool dayTmpHidden;
        private Vector2[] externalQuadLocal = new Vector2[4];

        // Dark ink for text on the pale paper panel (user feedback 20:00 #1:
        // game prefab text is light grey and unreadable on the beige surface).
        private static readonly Color InkColor = new Color(0.15f, 0.11f, 0.08f, 1f);

        private bool sceneHandled;
        private bool prepared;
        private string lastError;
        // [G / C-CHG-01 / E-M04] A generation token cancels any in-flight URL
        // resolve when a new URL is submitted. cid / directUrl are now local to
        // the coroutine, so overlapping chains can no longer overwrite each
        // other's parsed URL (the previously frozen B-13裁决 was lifted by the
        // user at 2026-11-15 17:57, see C 报告 §十二 C-CHG-01).
        private int resolveGeneration;
        private bool resolving;
        // [G-R5 / E2-L05] prepared / lastError carry the generation of the
        // chain that produced them. The prepare-wait loop only accepts a
        // callback from ITS OWN generation, so an old chain's async event can
        // no longer end the new chain's wait or supply its error text
        // (C-CHG-01 follow-up, F-R2 §13.3).
        private int activePrepareGen = -1;
        private int preparedGen = -1;
        private int lastErrorGen = -1;
        private string preparedUrl;
        private string errorUrl;
        // [G / E-H01] Last requested seek target, printed by seekCompleted so
        // the target and the actual time can be compared from results.txt.
        private double pendingSeekTarget = -1.0;
        private bool seekVerifyRunning;
        // [G-R5 / E2-L06] Seek sequence + in-flight counter: every seek starts
        // its own delayed verification with a LOCAL target, so a second seek
        // inside 0.6 s can no longer overwrite the first one's evidence, and
        // the running flag is always released in a finally block.
        private int seekSequence;
        private int seekVerifyInFlight;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
        }

        public void OnEnable()
        {
            _instance = this;
            ModHook.OnGameLoadedNormal += HandleGameLoaded;
            // User 21:26: while still playing the save, KEEP all data. Day-end
            // and leaving the store only PAUSE, then auto-resume on return.
            ModHook.OnGoingSleepEarly += OnGoingSleepEarlyHook;
            ModHook.OnLeavingStoreEarly += OnLeavingStoreEarlyHook;
            ModHook.OnWakingUpLate += OnWakingUpLateHook;
            ModHook.OnReturningStoreLate += OnReturningStoreLateHook;
            // Leaving the save loads the "EmporiumMenu" scene. The player
            // GameObject is DontDestroyOnLoad, so it must be stopped+released
            // explicitly here or the audio keeps playing in the main menu.
            SceneManager.sceneLoaded += OnSceneLoaded;
            // User 21:39: fire a pause signal when the ESC / Options pause menu
            // opens or closes (the game exposes no ModHook event for it).
            ApplyEscapePausePatches();
        }

        public void OnDisable()
        {
            // [G / E-L01] UnpatchAll can throw (e.g. a target method already
            // gone). Never let that skip Cleanup(), and always drop the static
            // instance so a stale Harmony postfix cannot reach a dead mod.
            try
            {
                ModHook.OnGameLoadedNormal -= HandleGameLoaded;
                ModHook.OnGoingSleepEarly -= OnGoingSleepEarlyHook;
                ModHook.OnLeavingStoreEarly -= OnLeavingStoreEarlyHook;
                ModHook.OnWakingUpLate -= OnWakingUpLateHook;
                ModHook.OnReturningStoreLate -= OnReturningStoreLateHook;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                if (harmony != null)
                {
                    harmony.UnpatchAll(harmony.Id);
                    harmony = null;
                    Add("HARMONY: escape-pause patches removed");
                    Flush("results.txt");
                }
            }
            catch (Exception e)
            {
                Add("ONDISABLE_UNPATCH_FAIL " + e.GetType().Name + ": " + e.Message);
                Flush("results.txt");
            }
            finally
            {
                Cleanup();
                _instance = null;
            }
        }

        private void HandleGameLoaded()
        {
            if (sceneHandled) return;
            sceneHandled = true;
            // ModHelper.StartCoroutine only works after the mod is registered,
            // so never start it from OnEnable (old probe pitfall #1).
            ModHelper.StartCoroutine(ModId, BootstrapFlow());
        }

        // --- save lifecycle (user 2026-11-14 21:26) -------------------------
        // Rule: while the save is being played, NEVER clear data (RT, URL,
        // playback state). Day-end / leaving the store only PAUSE, and resume
        // automatically on return. Only leaving the save (main-menu scene)
        // stops playback, releases the RT and restores the shop display.
        // V-03 evidence: keep the four ModHook events distinguishable and
        // removable by name; the shared handler receives the event name.
        private void OnGoingSleepEarlyHook()   { OnLifecyclePause("OnGoingSleepEarly"); }
        private void OnLeavingStoreEarlyHook() { OnLifecyclePause("OnLeavingStoreEarly"); }
        private void OnWakingUpLateHook()      { OnLifecycleResume("OnWakingUpLate"); }
        private void OnReturningStoreLateHook(){ OnLifecycleResume("OnReturningStoreLate"); }

        private void OnLifecyclePause(string hookName)
        {
            try
            {
                if (player == null) { Add("LIFECYCLE: " + hookName + " -> pause event, player null"); return; }
                if (player.isPlaying)
                {
                    player.Pause();
                    resumeAfterLifecycle = true;
                    Add("LIFECYCLE: " + hookName + " -> PAUSED; data kept, auto-resume armed=true");
                }
                else
                {
                    // Already paused by the user - never override their intent.
                    Add("LIFECYCLE: " + hookName + " -> pause event, player not playing (isPaused=" + player.isPaused + "); state kept");
                }
                Flush("results.txt");
            }
            catch (Exception e) { Add("LIFECYCLE_PAUSE_FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        private void OnLifecycleResume(string hookName)
        {
            try
            {
                if (resumeAfterLifecycle && player != null && player.isPaused)
                {
                    player.Play();
                    resumeAfterLifecycle = false;
                    Add("LIFECYCLE: " + hookName + " -> AUTO-RESUMED (armed true->false isPlaying=" + player.isPlaying + "); data kept");
                }
                else
                {
                    Add("LIFECYCLE: " + hookName + " -> no auto-resume (armed=" + resumeAfterLifecycle +
                        " isPaused=" + (player != null && player.isPaused) + ")");
                }
                Flush("results.txt");
            }
            catch (Exception e) { Add("LIFECYCLE_RESUME_FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            Add("LIFECYCLE: sceneLoaded name=" + s.name + " mode=" + mode);
            if (s.name == "EmporiumMenu") LeaveSaveCleanup("sceneLoaded:EmporiumMenu");
            Flush("results.txt");
        }

        // --- ESC / Options pause menu (user 2026-11-14 21:39) ---------------
        // Signal-based: Harmony patches the game's OWN open/close methods so
        // there is zero per-frame polling. A depth counter covers the nested
        // case where the Options sub-panel is opened from inside the ESC menu
        // (OnOption calls CloseEscapeMenu internally before opening Options).
        private void ApplyEscapePausePatches()
        {
            if (harmony != null) return;
            try
            {
                harmony = new Harmony("TV_BiliBili");
                Type esc = typeof(EscapeUIManager);
                Type opt = typeof(GameOptionUIManager);
                harmony.Patch(
                    esc.GetMethod("OpenEscapeMenu", BindingFlags.Public | BindingFlags.Instance),
                    postfix: new HarmonyMethod(typeof(TvBiliBiliMod).GetMethod("EscOpenPost", BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(
                    esc.GetMethod("CloseEscapeMenu", BindingFlags.Public | BindingFlags.Instance),
                    postfix: new HarmonyMethod(typeof(TvBiliBiliMod).GetMethod("EscClosePost", BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(
                    opt.GetMethod("OpenUI", BindingFlags.Public | BindingFlags.Instance),
                    postfix: new HarmonyMethod(typeof(TvBiliBiliMod).GetMethod("OptOpenPost", BindingFlags.NonPublic | BindingFlags.Static)));
                harmony.Patch(
                    opt.GetMethod("OnWindowClosed", BindingFlags.NonPublic | BindingFlags.Instance),
                    postfix: new HarmonyMethod(typeof(TvBiliBiliMod).GetMethod("OptClosedPost", BindingFlags.NonPublic | BindingFlags.Static)));
                Add("HARMONY: escape-pause patches applied (EscOpen/Close, OptOpen/Closed)");
                Flush("results.txt");
            }
            catch (Exception e)
            {
                Add("HARMONY_FAIL " + e.GetType().Name + ": " + e.Message);
                Flush("results.txt");
            }
        }

        // Static postfix trampolines -> instance handlers. Harmony postfixes
        // must be static; _instance carries our mod state across the boundary.
        private static void EscOpenPost()  { _instance?.PauseUiOpened("EscapeMenu"); }
        private static void EscClosePost() { _instance?.PauseUiClosed("EscapeMenu"); }
        private static void OptOpenPost()  { _instance?.PauseUiOpened("Options"); }
        private static void OptClosedPost(){ _instance?.PauseUiClosed("Options"); }

        private void PauseUiOpened(string src)
        {
            try
            {
                if (src == "EscapeMenu") escMenuOpen = true;
                else if (src == "Options") optMenuOpen = true;
                Add("LIFECYCLE: pause-ui open (" + src + ") esc=" + escMenuOpen + " opt=" + optMenuOpen);
                // An open arriving this frame means a close we just saw was NOT a
                // return to gameplay (Options opens from inside the ESC menu and
                // calls CloseEscapeMenu first). Drop any pending resume so we
                // never play a blip into a still-open overlay.
                if (pauseResumeCoroutine != null) { pauseResumeCoroutine = null; }
                if (player != null && player.isPlaying)
                {
                    player.Pause();
                    menuPaused = true;
                    Add("LIFECYCLE: PAUSED (escape/options menu); auto-resume armed");
                }
                Flush("results.txt");
            }
            catch (Exception e) { Add("ESC_PAUSE_FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        private void PauseUiClosed(string src)
        {
            try
            {
                if (src == "EscapeMenu") escMenuOpen = false;
                else if (src == "Options") optMenuOpen = false;
                Add("LIFECYCLE: pause-ui close (" + src + ") esc=" + escMenuOpen + " opt=" + optMenuOpen);
                // Only resume once BOTH overlays are gone. Defer by a short slice
                // so a same-frame re-open (Options right after CloseEscapeMenu)
                // keeps the resume pending instead of firing into Options.
                if (!escMenuOpen && !optMenuOpen && menuPaused && pauseResumeCoroutine == null)
                {
                    pauseResumeCoroutine = ModHelper.StartCoroutine(ModId, DeferredResume());
                }
                Flush("results.txt");
            }
            catch (Exception e) { Add("ESC_RESUME_FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        private IEnumerator DeferredResume()
        {
            yield return new WaitForSecondsRealtime(0.12f);
            pauseResumeCoroutine = null;
            try
            {
                // Re-check at fire time: a re-open within the slice cancels us.
                if (!escMenuOpen && !optMenuOpen && menuPaused && player != null && player.isPaused)
                {
                    player.Play();
                    menuPaused = false;
                    Add("LIFECYCLE: AUTO-RESUMED after escape/options menu (menuPaused true->false isPlaying=" + player.isPlaying + ")");
                }
                else
                {
                    Add("LIFECYCLE: deferred-resume skipped (esc=" + escMenuOpen +
                        " opt=" + optMenuOpen + " armed=" + menuPaused + ")");
                }
                Flush("results.txt");
            }
            catch (Exception e) { Add("ESC_RESUME_FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        // Belt-and-braces if sceneLoaded is missed. Uses the SAME single
        // criterion (main-menu scene name) so a day-end / leave-store flow can
        // never be mistaken for leaving the save.
        private IEnumerator SaveLifecycleGuard()
        {
            while (!leftSave)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                try
                {
                    if (SceneManager.GetActiveScene().name == "EmporiumMenu")
                    {
                        LeaveSaveCleanup("guard:scene=EmporiumMenu");
                        break;
                    }
                }
                catch (Exception e) { Add("GUARD_FAIL " + e.GetType().Name + ": " + e.Message); }
            }
        }

        private void LeaveSaveCleanup(string reason)
        {
            if (leftSave) return;
            leftSave = true;
            Add("LIFECYCLE: LEAVE SAVE (" + reason + ") -> stop, release RT, restore shop, drop stale refs");
            try
            {
                resumeAfterLifecycle = false;
                escMenuOpen = false;
                optMenuOpen = false;
                menuPaused = false;
                pauseResumeCoroutine = null;
                playerTabActive = false;
                if (player != null) { player.Stop(); Add("LIFECYCLE: player.Stop() isPlaying=" + player.isPlaying); }
                if (screen != null) screen.texture = null;
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; Add("LIFECYCLE: RT released"); }
                CancelInFlightResolve("LEAVE SAVE");
                pendingSeekTarget = -1.0;
                // [G / E-B01] The restore call is now bracketed by BEFORE /
                // AFTER lines inside this SAME LEAVE SAVE Flush chain. If the
                // old scene's dayTMP is already destroyed, the log says
                // "restore skipped" instead of silently doing nothing; no
                // fake "dayTMP restored" line is ever written (F §4.1).
                RestoreExternalDisplay("LEAVE SAVE:" + reason);
                if (externalOverlayGo != null) { UnityEngine.Object.Destroy(externalOverlayGo); externalOverlayGo = null; }
                if (playerGo != null) { UnityEngine.Object.Destroy(playerGo); playerGo = null; }
                dayTmp = null; externalWarp = null; dayTmpHidden = false;
                player = null; audioSource = null; screen = null;
                panelRoot = null; playerTabButton = null; calendarWatcher = null; calendar = null;
                urlInput = null; statusLabel = null; timeLabel = null; playPauseText = null;
                progressSlider = null; volumeSlider = null;
                tabHooksAdded = false; tabGeometrySaved = false;
                sceneHandled = false;   // rebuild on the next game load
                Add("LIFECYCLE: stale refs dropped; player rebuilds on next OnGameLoadedNormal");
            }
            catch (Exception e) { Add("LIFECYCLE_FAIL " + e.GetType().Name + ": " + e.Message); }
            Flush("results.txt");
        }

        // Product bootstrap (C 4.2): no launcher button, no auto-opened window,
        // no automatic run/screenshot probing. The 4th calendar tab is the only
        // entry point; the player UI is built lazily when the tab is first shown.
        private IEnumerator BootstrapFlow()
        {
            leftSave = false;
            resumeAfterLifecycle = false;
            escMenuOpen = false;
            optMenuOpen = false;
            menuPaused = false;
            pauseResumeCoroutine = null;
            ModHelper.StartCoroutine(ModId, SaveLifecycleGuard());
            yield return new WaitForSecondsRealtime(1.0f);
            Add("=== TV_BiliBili bootstrap (C v1: player as 4th calendar tab) ===");
            Add("unity=" + Application.unityVersion + " screen=" + Screen.width + "x" + Screen.height);
            Add("modDataFolder=" + ModHelper.GetModDataFolder(ModId));
            LogEnvironment();
            SetupPlayer();
            PrepareExternalDisplay();
            Flush();

            BuildCalendarTab();
            LogTabLayout();
            Flush();

            // C 4.2: clicking the shop display still opens the ORIGINAL calendar.
            // The 4th tab is closed by default; no auto-popup, no auto-play.
            Add("BOOTSTRAP: ready - no launcher button; player is calendar tab #4, closed by default");
            Flush("results.txt");
        }

        private void LogEnvironment()
        {
            try
            {
                Add("ENV: CustomUIManager.Instance=" + (CustomUIManager.Instance != null) +
                    " CreateWindow=" + (typeof(CustomUIManager).GetMethod("CreateWindow") != null) +
                    " SpawnElement=" + (typeof(CustomUIManager).GetMethod("SpawnElement") != null));
                Add("ENV: layers=" + string.Join(",", CustomUIManager.GetAvailableLayers().ToArray()));
                Add("ENV: AdvCalendarUIManager.Instance=" + (AdvCalendarUIManager.Instance != null) +
                    " UIPanel=" + (AdvCalendarUIManager.Instance != null && AdvCalendarUIManager.Instance.UIPanel != null ? AdvCalendarUIManager.Instance.UIPanel.name : "null"));
                Add("ENV: EmporiumOption.Instance=" + (EmporiumOption.Instance != null) +
                    " masterVolume=" + (EmporiumOption.Instance != null ? EmporiumOption.Instance.masterVolume.ToString("F2") : "n/a") +
                    " AudioListener.volume=" + AudioListener.volume.ToString("F2"));
                Add("ENV: VideoPlayer.Play=" + (typeof(VideoPlayer).GetMethod("Play") != null) +
                    " Pause=" + (typeof(VideoPlayer).GetMethod("Pause") != null) +
                    " canSetTime=" + (typeof(VideoPlayer).GetProperty("canSetTime") != null));
                // Canvas layering evidence for the 18:12/18:21 feedback: which
                // canvas owns the calendar display, and which one was the old
                // top-most launcher host.
                foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
                {
                    if (c.name == "CanvasWorldStoreInteract" || c.name == "CustomUIOverlayCanvas" || c.name == "CanvasPaperUI")
                    {
                        Add("ENV: canvas " + c.name + " order=" + c.sortingOrder +
                            " mode=" + c.renderMode + " raycaster=" + (c.GetComponent<GraphicRaycaster>() != null));
                    }
                }
                try
                {
                    StoreCalendar cal = StoreUIManager.Instance != null ? StoreUIManager.Instance.storeCalendar : null;
                    Canvas dc = (cal != null && cal.dayTMP != null && cal.dayTMP.canvas != null) ? cal.dayTMP.canvas.rootCanvas : null;
                    Add("ENV: calendarDisplayCanvas=" + (dc != null ? dc.name + " order=" + dc.sortingOrder : "null"));
                }
                catch (Exception ce)
                {
                    Add("ENV: calendarDisplayCanvas lookup fail " + ce.GetType().Name);
                }
            }
            catch (Exception e)
            {
                Add("ENV_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void SetupPlayer()
        {
            try
            {
                playerGo = new GameObject("TV_BiliBiliPlayer");
                UnityEngine.Object.DontDestroyOnLoad(playerGo);
                player = playerGo.AddComponent<VideoPlayer>();
                audioSource = playerGo.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
                EnsureRenderTexture();

                player.playOnAwake = false;
                player.source = VideoSource.Url;
                player.renderMode = VideoRenderMode.RenderTexture;
                player.targetTexture = rt;
                player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                player.controlledAudioTrackCount = 1;
                player.skipOnDrop = true;
                player.SetTargetAudioSource(0, audioSource);
                player.prepareCompleted += OnPrepared;
                player.started += OnStarted;
                player.errorReceived += OnError;
                player.seekCompleted += OnSeekCompleted;
                Add("PLAYER: created=" + (player != null) + " rt=" + (rt != null) + " audioSource=" + (audioSource != null));
            }
            catch (Exception e)
            {
                Add("PLAYER_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void EnsureRenderTexture()
        {
            if (rt != null) return;
            rt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            // [G-R2 / E-L04] C §4.3 requires a DARK idle video surface. A fresh
            // RenderTexture is undefined/transparent, so the pale beige calendar
            // panel showed through (live screenshot 06_player_tab.png, 19:59).
            // Explicitly clear it to dark ink; VideoPlayer overwrites it on play.
            ClearRenderTexture();
            if (player != null) player.targetTexture = rt;
            Add("RT: created/recreated 1280x720 idle=cleared-to-dark-ink");
        }

        private void ClearRenderTexture()
        {
            if (rt == null) return;
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0.09f, 0.07f, 0.06f, 1f));
            }
            catch (Exception e)
            {
                Add("RT_CLEAR_FAIL " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        // --- external in-store calendar display (original version's video surface) ---
        // User 20:02 clarified "我说的显示器内容是最初版本在外面显示的那个":
        // the video must ALSO be warped onto the tilted yellow screen of the
        // shop calendar display, not only shown inside the player panel.
        // Reuses the quad + WarpedTextureGraphic approach verified in stage B
        // (legacy probe, 12:30 / 13:07 runs).
        private void PrepareExternalDisplay()
        {
            try
            {
                StoreCalendar cal = StoreUIManager.Instance != null ? StoreUIManager.Instance.storeCalendar : null;
                dayTmp = cal != null ? cal.dayTMP : null;
                if (dayTmp == null) { Add("EXTDISPLAY: FAIL dayTMP null"); return; }
                Canvas canvas = dayTmp.canvas != null ? dayTmp.canvas.rootCanvas : null;
                if (canvas == null) { Add("EXTDISPLAY: FAIL canvas null"); return; }
                RectTransform root = (RectTransform)canvas.transform;

                Vector3[] wc = new Vector3[4];
                ((RectTransform)dayTmp.transform).GetWorldCorners(wc);
                // ScreenSpaceOverlay canvas: GetWorldCorners already ARE screen
                // pixels. Legacy probe used (Vector2)corners[i] when cam == null;
                // calling WorldToScreenPoint(null, ...) gives wrong values here.
                Vector2[] sc = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    sc[i] = (Vector2)wc[i];
                }
                // BuildFallbackQuad expands the dayTMP rect by ~10-15% to cover
                // the whole yellow screen (legacy probe measured 218x138).
                float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
                foreach (Vector2 p in sc)
                {
                    xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                    yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
                }
                float w = xMax - xMin, h = yMax - yMin;
                Vector2[] quad = new[]
                {
                    new Vector2(xMin - w * 0.13f, yMin - h * 0.13f),
                    new Vector2(xMin - w * 0.13f, yMax + h * 0.15f),
                    new Vector2(xMax + w * 0.10f, yMax + h * 0.15f),
                    new Vector2(xMax + w * 0.10f, yMin - h * 0.13f)
                };
                for (int i = 0; i < 4; i++)
                {
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(root, quad[i], null, out local);
                    externalQuadLocal[i] = local;
                }

                externalOverlayGo = new GameObject("DspPlayerExternalDisplay", typeof(RectTransform));
                RectTransform r = (RectTransform)externalOverlayGo.transform;
                r.SetParent(root, false);
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
                externalWarp = externalOverlayGo.AddComponent<WarpedTextureGraphic>();
                externalWarp.raycastTarget = false;   // keep A-8: clicking the display still opens the calendar
                externalWarp.color = Color.white;
                externalWarp.enabled = false;
                Add("EXTDISPLAY: prepared canvas=" + canvas.name + " order=" + canvas.sortingOrder +
                    " dayRect=" + w.ToString("F0") + "x" + h.ToString("F0") +
                    " quadLocal=" + externalQuadLocal[0] + externalQuadLocal[2]);
            }
            catch (Exception e)
            {
                Add("EXTDISPLAY_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void ShowExternalVideo()
        {
            try
            {
                if (externalWarp == null || rt == null) { Add("EXTDISPLAY: show skipped warp/rt null"); return; }
                externalWarp.SetQuad(externalQuadLocal[0], externalQuadLocal[1],
                    externalQuadLocal[2], externalQuadLocal[3], rt);
                externalWarp.enabled = true;
                if (dayTmp != null && dayTmp.gameObject.activeSelf)
                {
                    dayTmp.gameObject.SetActive(false);
                    dayTmpHidden = true;
                    Add("EXTDISPLAY: dayTMP hidden (video on shop display)");
                }
                Add("EXTDISPLAY: video enabled on shop calendar display");
            }
            catch (Exception e)
            {
                Add("EXTDISPLAY_SHOW_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        // [G / E-B01] Previously the restore silently did nothing when
        // dayTmpHidden==false or dayTmp had already been destroyed with the
        // unloaded scene, so the LEAVE SAVE chain had no readable evidence.
        // Log the before/after state AND the outcome in the same Flush chain.
        // F §3 裁定原则 6: never fabricate a "restored" line to close V-04.
        private void RestoreExternalDisplay(string context = "unspecified")
        {
            bool warpValid = externalWarp != null;
            bool dayTmpValid = dayTmp != null;   // Unity's == / != already accounts for destroyed objects
            bool activeBefore = dayTmpValid && dayTmp.gameObject != null && dayTmp.gameObject.activeSelf;
            Add("EXTDISPLAY: restore begin ctx=" + context +
                " dayTmpHidden=" + dayTmpHidden + " dayTmpValid=" + dayTmpValid +
                " activeBefore=" + activeBefore + " warpValid=" + warpValid);
            string result;
            try
            {
                if (externalWarp != null) externalWarp.enabled = false;
                if (dayTmpHidden && dayTmp != null)
                {
                    dayTmp.gameObject.SetActive(true);
                    dayTmpHidden = false;
                    result = "restored";
                    Add("EXTDISPLAY: dayTMP restored (ctx=" + context + ")");
                }
                else if (!dayTmpHidden)
                {
                    result = "restore skipped (reason=dayTmpHidden=false; nothing was hidden)";
                    Add("EXTDISPLAY: restore skipped ctx=" + context + " reason=dayTmpHidden=false");
                }
                else
                {
                    result = "restore skipped (reason=dayTMP null/destroyed with the unloaded scene)";
                    Add("EXTDISPLAY: restore skipped ctx=" + context + " reason=dayTMP-unavailable");
                }
            }
            catch (Exception e)
            {
                result = "restore failed (" + e.GetType().Name + ")";
                Add("EXTDISPLAY_RESTORE_FAIL " + e.GetType().Name + ": " + e.Message);
            }
            bool activeAfter = dayTmp != null && dayTmp.gameObject != null && dayTmp.gameObject.activeSelf;
            Add("EXTDISPLAY: restore end ctx=" + context + " result=" + result +
                " dayTmpHidden=" + dayTmpHidden + " activeAfter=" + activeAfter +
                " warpEnabled=" + (externalWarp != null && externalWarp.enabled));
        }

        private void OnPrepared(VideoPlayer vp)
        {
            prepared = true;
            // [G-R5 / E2-L05] Tag the callback with the generation that asked
            // for this Prepare; the wait loop ignores callbacks of other chains.
            preparedGen = activePrepareGen;
            preparedUrl = vp != null ? vp.url : null;
            Uri pu;
            Add("EVENT prepareCompleted gen=" + preparedGen +
                " host=" + (IsPlayableHttpUrl(preparedUrl, out pu) ? pu.Host : "-") +
                " w=" + vp.width + " h=" + vp.height + " len=" + vp.length.ToString("F2") +
                " fps=" + vp.frameRate.ToString("F2") + " audioTracks=" + vp.audioTrackCount + " frameCount=" + vp.frameCount);
        }

        private void OnStarted(VideoPlayer vp)
        {
            Add("EVENT started frame=" + vp.frame + " time=" + vp.time.ToString("F2"));
        }

        private void OnError(VideoPlayer vp, string message)
        {
            lastError = message;
            // [G-R5 / E2-L05] Same generation tag as OnPrepared; an error from
            // an already-stale chain must not decide the new chain's outcome.
            lastErrorGen = activePrepareGen;
            errorUrl = vp != null ? vp.url : null;
            Add("EVENT errorReceived gen=" + lastErrorGen + ": " + message);
        }

        private void OnSeekCompleted(VideoPlayer vp)
        {
            // [G / E-H01] Log the requested target next to the actual time.
            // NOTE (verified live 20:07 / 20:28): seekCompleted fires with the
            // PRE-seek time/frame on URL sources, so this line alone cannot
            // decide whether a seek worked. SEEK_VERIFY (delayed read of
            // player.time) is the authoritative evidence for C-7 / V-06.
            Add("EVENT seekCompleted time=" + vp.time.ToString("F2") + " frame=" + vp.frame +
                " target=" + pendingSeekTarget.ToString("F2") +
                " delta=" + (pendingSeekTarget >= 0 ? (vp.time - pendingSeekTarget).ToString("F2") : "n/a") +
                " (pre-seek snapshot; see SEEK_VERIFY)");
        }

        private IEnumerator RequestText(string url, float timeout, Action<UnityWebRequest> done)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = (int)timeout;
                req.SetRequestHeader("User-Agent", UserAgent);
                req.SetRequestHeader("Referer", Referer);
                yield return req.SendWebRequest();
                // [G / E-M05] new Uri(url) could throw before the callback ran.
                Uri reqUri;
                string host = Uri.TryCreate(url, UriKind.Absolute, out reqUri) ? reqUri.Host : "-";
                Add("HTTP " + host + " result=" + req.result + " code=" + req.responseCode + " error=" + (req.error ?? "-"));
                if (done != null)
                {
                    try { done(req); }
                    catch (Exception e) { Add("HTTP parse fail " + e.GetType().Name + ": " + e.Message); }
                }
            }
        }

        // [G / E-M05 + E-M04] Both parsers are now defensive AND pure: they
        // return the parsed value instead of writing shared instance fields,
        // so overlapping resolve chains cannot overwrite each other. Any
        // malformed / non-JSON / risk-control response yields null, which the
        // caller maps to the already frozen STATUS error text (C-9).
        private string ParseViewCid(UnityWebRequest req)
        {
            try
            {
                if (req == null || req.result != UnityWebRequest.Result.Success) return null;
                JObject json = JObject.Parse(req.downloadHandler.text);
                if (json == null) return null;
                JObject data = json["data"] as JObject;
                string parsedCid = data != null && data["cid"] != null ? data["cid"].ToString() : null;
                Add("VIEW code=" + json.Value<int?>("code") + " cid=" + parsedCid +
                    " title=" + (data != null ? data.Value<string>("title") : "-"));
                return parsedCid;
            }
            catch (Exception e)
            {
                Add("VIEW_PARSE_FAIL " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        private string ParsePlayUrlDirect(UnityWebRequest req)
        {
            try
            {
                if (req == null || req.result != UnityWebRequest.Result.Success) return null;
                JObject json = JObject.Parse(req.downloadHandler.text);
                if (json == null) return null;
                JObject data = json["data"] as JObject;
                JArray durl = data != null ? data["durl"] as JArray : null;
                Add("PLAYURL code=" + json.Value<int?>("code") + " quality=" +
                    (data != null && data["quality"] != null ? data.Value<int?>("quality").ToString() : "-") +
                    " durlCount=" + (durl != null ? durl.Count : 0));
                if (durl == null || durl.Count == 0) return null;
                string candidate = durl[0].Value<string>("url");
                Uri parsed;
                if (!IsPlayableHttpUrl(candidate, out parsed))
                {
                    Add("PLAYURL REJECT: invalid/relative/non-http direct url head=" +
                        (string.IsNullOrEmpty(candidate) ? "-" : candidate.Substring(0, Math.Min(90, candidate.Length))));
                    return null;
                }
                Add("PLAYURL host=" + parsed.Host + " urlHead=" + candidate.Substring(0, Math.Min(90, candidate.Length)));
                return candidate;
            }
            catch (Exception e)
            {
                Add("PLAYURL_PARSE_FAIL " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        private static bool IsPlayableHttpUrl(string candidate, out Uri parsed)
        {
            parsed = null;
            if (string.IsNullOrEmpty(candidate)) return false;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out parsed)) return false;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
            return !string.IsNullOrEmpty(parsed.Host);
        }

        // [G / C-CHG-01] Minimal concurrency guard. A new URL bumps the
        // generation; every await point of the old chain checks it and bails
        // out before touching player / status / UI.
        private bool IsStaleResolve(int generation)
        {
            // [G / C-CHG-01] A chain is stale when a newer generation exists or
            // when resolution has already been cancelled / finished. Reading
            // the "解析中" flag here is what makes it a real guard, not just a
            // bookkeeping field.
            return generation != resolveGeneration || !resolving;
        }

        private void CancelInFlightResolve(string reason)
        {
            int previous = resolveGeneration;
            resolveGeneration++;
            resolving = false;
            Add("RESOLVE: cancelled gen=" + previous + " -> gen=" + resolveGeneration +
                " resolving=false (" + reason + ")");
        }

        // --- A section 12: add a 4th tab to the ORIGINAL calendar panel ---
        // Reuse the game's own tab button + section-switching logic. Nothing is
        // cloned as a separate window; the calendar's own title bar and close
        // button are kept (A 12.2 "标题栏、关闭按钮由日历面板自带").
        private void BuildCalendarTab()
        {
            try
            {
                calendar = AdvCalendarUIManager.Instance;
                if (calendar == null || calendar.UIPanel == null)
                {
                    Add("TAB: FAIL AdvCalendarUIManager.Instance or UIPanel null");
                    return;
                }
                GameObject panel = calendar.UIPanel;
                Transform toggleRoot = panel.transform.Find("TopToggleGroup");
                Transform srcBtn = toggleRoot != null ? toggleRoot.Find("CalendarButton") : null;
                if (toggleRoot == null || srcBtn == null)
                {
                    Add("TAB: FAIL TopToggleGroup/CalendarButton not found (children=" +
                        (toggleRoot != null ? toggleRoot.childCount.ToString() : "-") + ")");
                    return;
                }
                Add("TAB: panel=" + panel.name + " active=" + panel.activeSelf +
                    " toggleChildren=" + toggleRoot.childCount +
                    " horizontalGroup=" + (toggleRoot.GetComponent<HorizontalLayoutGroup>() != null));

                // User 21:12 #1: the cloned 播放器 tab had near-black text while
                // the three stock tabs use pale text on a dark-red button. Sample
                // the STOCK colour instead of reusing InkColor (which is meant for
                // widgets on the pale paper background, not for tab buttons).
                TMP_Text srcText = srcBtn.GetComponentInChildren<TMP_Text>(true);
                tabTextColor = srcText != null ? srcText.color : Color.white;
                Add("TAB: stockTabTextColor=" + tabTextColor + " (InkColor=" + InkColor + " is for paper widgets only)");

                // Clone the stock tab button. The group's HorizontalLayoutGroup
                // lays it out, so NO hand-picked position or size (31_开发约定 #2).
                GameObject tabGo = UnityEngine.Object.Instantiate(srcBtn.gameObject, toggleRoot, false);
                tabGo.name = "PlayerTabButton";
                tabGo.SetActive(true);
                playerTabButton = tabGo;

                // Stock buttons carry LocalizeStringEvent / TMPLocalizer that
                // overwrite the label at runtime; disable them by type name so
                // no compile-time reference to those components is needed.
                int localized = 0;
                foreach (MonoBehaviour mb in tabGo.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb != null && mb.GetType().Name.IndexOf("Localiz", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        mb.enabled = false;
                        localized++;
                    }
                }
                TMP_Text tabText = tabGo.GetComponentInChildren<TMP_Text>(true);
                RefreshTabLabel();

                Button tabBtn = tabGo.GetComponent<Button>();
                if (tabBtn == null) { Add("TAB: FAIL cloned button has no Button"); return; }
                // Instantiate copies the stock PERSISTENT onClick call (which
                // would switch back to the calendar section). Button.onClick has
                // a setter, so replacing the whole event clears persistent AND
                // runtime listeners. RemoveAllListeners() would NOT suffice.
                tabBtn.onClick = new Button.ButtonClickedEvent();
                tabBtn.onClick.AddListener(ShowPlayerTab);

                // Switching back to any stock tab must hide our tab body.
                // AddListener appends; the stock callbacks stay untouched.
                if (!tabHooksAdded)
                {
                    if (calendar.calendarButton != null) calendar.calendarButton.onClick.AddListener(HidePlayerTab);
                    if (calendar.nightlyReportButton != null) calendar.nightlyReportButton.onClick.AddListener(HidePlayerTab);
                    if (calendar.statusButton != null) calendar.statusButton.onClick.AddListener(HidePlayerTab);
                    tabHooksAdded = true;
                }

                // The 4th tab body: a plain child of the ORIGINAL panel, last
                // sibling so it draws above the three stock sections.
                panelRoot = new GameObject("DspPlayerTabContent", typeof(RectTransform));
                RectTransform pr = (RectTransform)panelRoot.transform;
                pr.SetParent(panel.transform, false);
                pr.SetAsLastSibling();
                pr.anchorMin = new Vector2(0.5f, 0.5f);
                pr.anchorMax = new Vector2(0.5f, 0.5f);
                pr.pivot = new Vector2(0.5f, 0.5f);
                pr.anchoredPosition = new Vector2(0f, -30f);   // leave room for the tab bar
                pr.sizeDelta = new Vector2(1200f, 760f);
                panelRoot.SetActive(false);

                // Calendar open/close must also leave the player tab.
                calendarWatcher = panel.AddComponent<CalendarTabWatcher>();
                calendarWatcher.onOpen = HidePlayerTab;
                calendarWatcher.onClose = HidePlayerTab;

                // User 20:52 #1: with four buttons the row was off-centre.
                CenterToggleGroup(toggleRoot, panel);

                Add("TAB: created 4th tab text=" + (tabText != null ? tabText.text : "-") +
                    " color=" + (tabText != null ? tabText.color.ToString() : "-") +
                    " stockColor=" + tabTextColor +
                    " localizedDisabled=" + localized +
                    " btnSize=" + ((RectTransform)tabGo.transform).rect.width.ToString("F0") + "x" +
                    ((RectTransform)tabGo.transform).rect.height.ToString("F0") +
                    " content=1200x760 active=False hooks=" + tabHooksAdded);
            }
            catch (Exception e)
            {
                Add("TAB_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void ShowPlayerTab()
        {
            try
            {
                Add("TAB: SHOW 播放器 tab clicked");
                RefreshTabLabel();
                if (panelRoot == null) { Add("TAB: show skipped, content missing"); return; }
                // Reuse the game's own section switch: hides all 3 stock
                // sections and resets their button colours (A 12.2).
                if (calendar != null) calendar.OnAnySectionButtonClicked();
                if (playerTabButton != null)
                {
                    Image im = playerTabButton.GetComponent<Image>();
                    if (im != null && calendar != null) im.color = calendar.selectedColor;
                }
                playerTabActive = true;
                panelRoot.SetActive(true);

                // Defensive rebind (idempotent). Per user 20:00 #2 + A 12.2 note
                // (20:34 "沿用实现工程师阶段 B 成果"), leaving the tab does NOT
                // stop playback or release the RT: the video keeps rendering to
                // the shop display. So the picture and all text are still here.
                EnsureRenderTexture();
                if (player != null) player.targetTexture = rt;
                if (screen == null) BuildPlayerContent();
                else
                {
                    // [G-R5 / E2-L06] Re-assert the dark idle surface (C §4.3)
                    // when the tab is reopened before any video has ever been
                    // prepared. A playing / paused / finished video keeps its
                    // last frame, so playback and the C-5 evidence are
                    // untouched.
                    if (player == null || !player.isPrepared)
                    {
                        ClearRenderTexture();
                        Add("RT: idle dark re-asserted on tab reopen (no video prepared)");
                    }
                    screen.texture = rt;
                    screen.color = Color.white;
                    if (urlInput != null) { urlInput.text = lastUrlText; inputUrlText = lastUrlText; }
                    if (statusLabel != null) statusLabel.text = lastStatusText;
                    if (timeLabel != null) timeLabel.text = lastTimeText;
                }
                SyncPlayPauseLabel();
                if (!progressLoopRunning && panelRoot != null)
                {
                    ModHelper.StartCoroutine(ModId, ProgressLoop());
                }
                Flush("results.txt");
            }
            catch (Exception e)
            {
                Add("TAB_SHOW_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void HidePlayerTab()
        {
            try
            {
                // Re-assert before the early return: the calendar's first
                // OnEnable hits that path, and a stock localizer may already
                // have rewritten the cloned button's label by then.
                RefreshTabLabel();
                if (!playerTabActive && panelRoot != null && !panelRoot.activeSelf) return;
                Add("TAB: HIDE (save still active -> keep RT / playback / URL / shop display)");
                playerTabActive = false;
                if (panelRoot != null) panelRoot.SetActive(false);
                if (playerTabButton != null)
                {
                    Image im = playerTabButton.GetComponent<Image>();
                    if (im != null && calendar != null) im.color = calendar.normalColor;
                }
                // User 21:26 settled the earlier A 12.3 vs A 12.2-note ambiguity:
                // while the save is active, NEVER clear data. Switching tabs or
                // closing the calendar only hides the tab body. Day-end and
                // leaving the store PAUSE via ModHook (auto-resume on return).
                // Only leaving the save (EmporiumMenu scene) clears everything
                // in LeaveSaveCleanup().
                Flush("results.txt");
            }
            catch (Exception e)
            {
                Add("TAB_HIDE_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void SyncPlayPauseLabel()
        {
            if (playPauseText == null) return;
            if (player == null) { playPauseText.text = "播放"; return; }
            playPauseText.text = player.isPlaying ? "暂停" : (player.isPaused ? "继续" : "播放");
        }

        // LocalizeStringEvent/TMPLocalizer on the cloned stock button are only
        // disabled (their Awake may still run once), so re-assert the label on
        // every tab switch. Avoids a stock localization key replacing 播放器.
        private void RefreshTabLabel()
        {
            if (playerTabButton == null) return;
            TMP_Text t = playerTabButton.GetComponentInChildren<TMP_Text>(true);
            if (t != null) { t.text = "播放器"; t.color = tabTextColor; }
        }

        // User 20:52 #1: "四个页签按钮居中，当前四个页签到左右边界的距离不同".
        // Keep the game's own HorizontalLayoutGroup - just centre its children
        // and centre the group in the panel. No hand-picked button coordinates.
        private void CenterToggleGroup(Transform toggleRoot, GameObject panel)
        {
            try
            {
                RectTransform trt = (RectTransform)toggleRoot;
                HorizontalLayoutGroup hlg = toggleRoot.GetComponent<HorizontalLayoutGroup>();
                if (hlg == null) { Add("TABGEO: FAIL no HorizontalLayoutGroup"); return; }
                if (!tabGeometrySaved)
                {
                    origTabAlignment = hlg.childAlignment;
                    origTabSize = trt.sizeDelta;
                    origTabPos = trt.anchoredPosition;
                    tabGeometrySaved = true;
                }

                // Packing from the left left unequal side margins once a 4th
                // button was added. Change ONLY the horizontal component and
                // keep the stock vertical row, otherwise the tab bar would
                // shift down and make the video overlap worse.
                TextAnchor a = origTabAlignment;
                if (a == TextAnchor.UpperLeft || a == TextAnchor.UpperCenter || a == TextAnchor.UpperRight)
                    a = TextAnchor.UpperCenter;
                else if (a == TextAnchor.LowerLeft || a == TextAnchor.LowerCenter || a == TextAnchor.LowerRight)
                    a = TextAnchor.LowerCenter;
                else
                    a = TextAnchor.MiddleCenter;
                hlg.childAlignment = a;

                // Widen the group to exactly fit the four buttons so the old
                // 800px container does not clip or skew the centred row.
                float total = hlg.padding.left + hlg.padding.right;
                int n = toggleRoot.childCount;
                for (int i = 0; i < n; i++)
                {
                    RectTransform c = toggleRoot.GetChild(i) as RectTransform;
                    if (c == null) continue;
                    LayoutElement le = c.GetComponent<LayoutElement>();
                    float w = (le != null && le.preferredWidth > 0f) ? le.preferredWidth : c.sizeDelta.x;
                    total += w;
                }
                total += Mathf.Max(0, n - 1) * hlg.spacing;
                trt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, total);
                Canvas.ForceUpdateCanvases();

                // Align group centre with panel centre (world coords are screen
                // pixels for a ScreenSpaceOverlay canvas).
                RectTransform panelRt = (RectTransform)panel.transform;
                Vector3[] pc = new Vector3[4]; panelRt.GetWorldCorners(pc);
                Vector3[] gc = new Vector3[4]; trt.GetWorldCorners(gc);
                float panelCx = (pc[0].x + pc[2].x) * 0.5f;
                float groupCx = (gc[0].x + gc[2].x) * 0.5f;
                float dx = panelCx - groupCx;
                trt.anchoredPosition += new Vector2(dx, 0f);
                Canvas.ForceUpdateCanvases();

                Rect pr = ScreenRectOf(panelRt);
                Rect firstR = n > 0 ? ScreenRectOf((RectTransform)toggleRoot.GetChild(0)) : pr;
                Rect lastR = n > 0 ? ScreenRectOf((RectTransform)toggleRoot.GetChild(n - 1)) : pr;
                float leftGap = firstR.xMin - pr.xMin;
                float rightGap = pr.xMax - lastR.xMax;
                Add("TABGEO: children=" + n + " spacing=" + hlg.spacing +
                    " oldW=" + origTabSize.x.ToString("F0") + " newW=" + total.ToString("F0") +
                    " align=" + origTabAlignment + "->" + hlg.childAlignment +
                    " centerDx=" + dx.ToString("F1"));
                Add("TABGEO: panelRect=" + pr + " leftGap=" + leftGap.ToString("F1") +
                    " rightGap=" + rightGap.ToString("F1") + " diff=" + Mathf.Abs(leftGap - rightGap).ToString("F1"));
                for (int i = 0; i < n; i++)
                {
                    RectTransform c = toggleRoot.GetChild(i) as RectTransform;
                    if (c != null) Add("TABGEO btn[" + i + "]=" + c.name + " " + ScreenRectOf(c));
                }
            }
            catch (Exception e)
            {
                Add("TABGEO_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        // A 12: no panel cloning at all. The player body is a child of the
        // ORIGINAL AdvCalendarUIManager.UIPanel (built in BuildCalendarTab),
        // switched on/off together with the stock sections. The old clone,
        // index-path hiding and separate window code were removed on 20:36.

        private void DumpHierarchy(Transform t, int depth)
        {
            if (t == null || depth > 7) return;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < depth; i++) sb.Append("  ");
            sb.Append(t.name);
            List<string> comps = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                if (n == "RectTransform" || n == "CanvasRenderer" || n == "Transform") continue;
                comps.Add(n);
            }
            sb.Append(" [" + string.Join(",", comps) + "]");
            RectTransform rt = t as RectTransform;
            if (rt != null)
            {
                sb.Append(" size=" + rt.rect.width.ToString("F0") + "x" + rt.rect.height.ToString("F0") +
                    " active=" + t.gameObject.activeSelf);
            }
            Add("HDR " + sb);
            for (int i = 0; i < t.childCount; i++) DumpHierarchy(t.GetChild(i), depth + 1);
        }

        // Absolute layout. Every control gets explicit anchors, pivot,
        // anchoredPosition and sizeDelta. NO LayoutGroup anywhere on the content
        // tree: LayoutGroup overwrites sizeDelta on rebuild, which is exactly why
        // the previous "明文尺寸" (SetPreferredSize / LayoutElement) never took
        // effect (19:34 run: all controls measured 160x32, video 100x100).
        private void BuildPlayerContent()
        {
            try
            {
                if (panelRoot == null) { Add("BUILD: panelRoot null"); return; }
                placed.Clear();
                EnsureRenderTexture();
                if (player != null) player.targetTexture = rt;

                GameObject content = new GameObject("DspPlayerContent", typeof(RectTransform));
                RectTransform crt = (RectTransform)content.transform;
                crt.SetParent(panelRoot.transform, false);
                crt.anchorMin = new Vector2(0.5f, 0.5f);
                crt.anchorMax = new Vector2(0.5f, 0.5f);
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = Vector2.zero;
                crt.sizeDelta = new Vector2(1200f, 760f);
                Add("BUILD: layout=ABSOLUTE content=1200x760 (A12: video/url/progress/control)");

                // 1) Video area (A 12.2). Idle surface is dark ink, not pure white
                //    (user 20:00 #2 "关闭重新打开后播放区纯白一片").
                GameObject area = new GameObject("VideoArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                area.transform.SetParent(crt, false);
                screen = area.GetComponent<RawImage>();
                screen.texture = rt;
                screen.color = Color.white;
                Place("video", area, 0f, 90f, 800f, 450f);

                // 2) URL row: ONE centered input, no separate 播放 button
                //    (user 20:31 #1 "播放按钮并入暂停按钮，输入框居中").
                GameObject inputGo = Spawn("dsp_url_input", CustomUIManager.ElementKind.Input, crt);
                urlInput = inputGo != null ? inputGo.GetComponentInChildren<TMP_InputField>(true) : null;
                if (urlInput != null)
                {
                    urlInput.text = lastUrlText;
                    inputUrlText = lastUrlText;
                    urlInput.onValueChanged.AddListener(delegate (string v) { inputUrlText = v; lastUrlText = v; });
                }
                ApplyInk(inputGo);
                Place("url_input", inputGo, 0f, -175f, 620f, 44f);

                // 3) Progress slider with a frame (user 20:31 #2). The shop scene
                //    leaves sliderPrefab/progressBarPrefab unassigned (GameLog
                //    "No prefab assigned for 'slider'/'progressbar'"), so per
                //    31_开发约定 #2 we fall back to the engine-native Slider.
                progressSlider = MakeSlider(crt, "dsp_seek", 0f, -235f, 900f, 22f, 0f);
                if (progressSlider != null) progressSlider.onValueChanged.AddListener(OnSeekSlider);
                Track("seek", progressSlider != null ? progressSlider.gameObject : null);

                // 4) Control row: merged 播放/暂停/继续 + time + volume(frame).
                GameObject ppGo = Spawn("dsp_playpause_btn", CustomUIManager.ElementKind.Button, crt);
                playPauseText = ppGo != null ? ppGo.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                Button ppBtn = ppGo != null ? ppGo.GetComponent<Button>() : null;
                if (ppBtn != null) ppBtn.onClick.AddListener(OnPlayPauseClicked);
                ApplyInk(ppGo);
                Place("playpause_btn", ppGo, -380f, -285f, 160f, 44f);
                SyncPlayPauseLabel();

                GameObject timeGo = Spawn("dsp_time", CustomUIManager.ElementKind.Label, crt);
                timeLabel = timeGo != null ? timeGo.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                if (timeLabel != null) timeLabel.text = lastTimeText;
                ApplyInk(timeGo);
                Place("time", timeGo, -180f, -285f, 200f, 40f);

                volumeSlider = MakeSlider(crt, "dsp_volume", 260f, -285f, 240f, 22f, 1f);
                if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnVolumeSlider);
                Track("volume", volumeSlider != null ? volumeSlider.gameObject : null);

                // 5) "音量" text label, hugging the left edge of the volume slider.
                GameObject volLabelGo = Spawn("dsp_volume_label", CustomUIManager.ElementKind.Label, crt);
                TextMeshProUGUI volLabel = volLabelGo != null ? volLabelGo.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                if (volLabel != null) { volLabel.text = "音量"; volLabel.alignment = TextAlignmentOptions.Right; }
                ApplyInk(volLabelGo);
                Place("volume_label", volLabelGo, 109f, -285f, 56f, 32f);

                GameObject statusGo = Spawn("dsp_status", CustomUIManager.ElementKind.Label, crt);
                statusLabel = statusGo != null ? statusGo.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                if (statusLabel != null) statusLabel.text = lastStatusText;
                ApplyInk(statusGo);
                Place("status", statusGo, 0f, -340f, 400f, 32f);

                // A 12.2: title bar / close button come from the calendar panel;
                // the tab body deliberately has no extra close control.
                Add("BUILD: done input=" + (urlInput != null) + " playPauseBtn=" + (ppBtn != null) +
                    " status=" + (statusLabel != null) + " time=" + (timeLabel != null) +
                    " seekSlider=" + (progressSlider != null) + " volumeSlider=" + (volumeSlider != null) +
                    " rawImage=" + (screen != null) + " closeButton=calendar-native");
                // [G-R5 / E2-M01] "就绪" is the field initial value of
                // lastStatusText and never went through ShowStatus, so C §4.5's
                // first status had no STATUS line. Log-only addition; the state
                // machine and all ten texts are unchanged.
                Add("STATUS: 就绪");
                foreach (KeyValuePair<string, RectTransform> kv in placed)
                {
                    Add("TARGET " + kv.Key + " size=" + kv.Value.sizeDelta.x.ToString("F0") + "x" +
                        kv.Value.sizeDelta.y.ToString("F0") + " pos=" + kv.Value.anchoredPosition);
                }
                DumpHierarchy(panelRoot.transform, 0);
                if (!layoutVerifyRunning)
                {
                    ModHelper.StartCoroutine(ModId, VerifyLayoutCo());
                }
            }
            catch (Exception e)
            {
                Add("BUILD_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Game prefabs use light-grey text designed for dark windows; on the
        // beige cloned calendar paper it is unreadable (user 20:00 #1).
        private static void ApplyInk(GameObject go)
        {
            if (go == null) return;
            foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(true))
            {
                t.color = InkColor;
            }
        }

        private GameObject Spawn(string name, CustomUIManager.ElementKind kind, Transform parent)
        {
            GameObject go = CustomUIManager.Instance != null
                ? CustomUIManager.Instance.SpawnElement(kind, parent, null, name) : null;
            Add("SPAWN " + name + " kind=" + kind + " ok=" + (go != null));
            return go;
        }

        // Sets anchors/pivot/position/size explicitly and removes any
        // LayoutElement the prefab may carry, so no layout pass can resize it.
        private void Place(string tag, GameObject go, float x, float y, float w, float h)
        {
            if (go == null) { Add("PLACE " + tag + " FAILED (spawn null)"); return; }
            RectTransform r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le != null) le.enabled = false;
            ContentSizeFitter csf = go.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.enabled = false;
            Track(tag, go);
        }

        private void Track(string tag, GameObject go)
        {
            if (go != null) placed.Add(new KeyValuePair<string, RectTransform>(tag, (RectTransform)go.transform));
        }

        // Engine-native Slider (not the game prefab, which is unassigned here).
        private Slider MakeSlider(Transform parent, string name, float x, float y, float w, float h, float value)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Slider));
            root.transform.SetParent(parent, false);
            RectTransform rr = (RectTransform)root.transform;
            rr.anchorMin = new Vector2(0.5f, 0.5f);
            rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.anchoredPosition = new Vector2(x, y);
            rr.sizeDelta = new Vector2(w, h);

            // Visible border (user 20:31 #2 "进度条和音量滑块加个边框").
            // A 3px outer Image drawn FIRST, so the slider background covers
            // its centre and only the rim shows. InkColor matches the text.
            GameObject frame = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frame.transform.SetParent(rr, false);
            frame.transform.SetAsFirstSibling();
            Image frameImg = frame.GetComponent<Image>();
            frameImg.color = InkColor;
            frameImg.raycastTarget = false;
            RectTransform fr = (RectTransform)frame.transform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(-3f, -3f);
            fr.offsetMax = new Vector2(3f, 3f);

            GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(rr, false);
            Stretch((RectTransform)bg.transform, 0f, 0f);
            bg.GetComponent<Image>().color = new Color(0.20f, 0.18f, 0.15f, 1f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(rr, false);
            Stretch((RectTransform)fillArea.transform, 0f, 0f);
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            Stretch((RectTransform)fill.transform, 0f, 0f);
            fill.GetComponent<Image>().color = new Color(0.78f, 0.58f, 0.24f, 1f);

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(rr, false);
            Stretch((RectTransform)handleArea.transform, 8f, 8f);
            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform hr = (RectTransform)handle.transform;
            hr.anchorMin = new Vector2(0f, 0f);
            hr.anchorMax = new Vector2(0f, 1f);
            hr.pivot = new Vector2(0.5f, 0.5f);
            hr.sizeDelta = new Vector2(16f, 0f);
            handle.GetComponent<Image>().color = new Color(0.95f, 0.88f, 0.70f, 1f);

            Slider s = root.GetComponent<Slider>();
            s.fillRect = (RectTransform)fill.transform;
            s.handleRect = hr;
            s.targetGraphic = handle.GetComponent<Image>();
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = 0f;
            s.maxValue = 1f;
            s.value = value;
            Add("MAKESLIDER " + name + " size=" + w.ToString("F0") + "x" + h.ToString("F0") +
                " frame=3px frameSize=" + (w + 6f).ToString("F0") + "x" + (h + 6f).ToString("F0"));
            return s;
        }

        private static void Stretch(RectTransform r, float padX, float padY)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(padX, padY);
            r.offsetMax = new Vector2(-padX, -padY);
        }

        // Measure AFTER Unity has rebuilt layouts. This is the evidence that the
        // explicit sizes actually took effect (the 19:34 run never did this).
        private IEnumerator VerifyLayoutCo()
        {
            layoutVerifyRunning = true;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Add("VERIFY: after layout rebuild");
            foreach (KeyValuePair<string, RectTransform> kv in placed)
            {
                RectTransform r = kv.Value;
                if (r == null) { Add("VERIFY " + kv.Key + "=destroyed"); continue; }
                Add("VERIFY " + kv.Key + " size=" + r.rect.width.ToString("F0") + "x" + r.rect.height.ToString("F0") +
                    " pos=" + r.anchoredPosition + " screen=" + ScreenRectOf(r));
            }
            // User 20:52 #2/#3: log real margins so "video covers the tab bar"
            // and "status text outside the paper area" are decidable from the
            // results file, not only from a screenshot.
            if (calendar != null && calendar.UIPanel != null)
            {
                RectTransform prt = (RectTransform)calendar.UIPanel.transform;
                Rect pr = ScreenRectOf(prt);
                Add("PANELGEO panel=" + pr);
                Transform tg = prt.Find("TopToggleGroup");
                if (tg != null)
                {
                    Rect gr = ScreenRectOf((RectTransform)tg);
                    // [G / E-L03] 复盘 #26: the CONTAINER lower edge (y=823/412)
                    // is NOT the criterion. C §4.3 measures the gap against the
                    // TAB BUTTON lower edge (875 / 437.5). Log that edge so the
                    // 25/50px gap cannot be misread as an overlap again.
                    RectTransform firstBtn = tg.childCount > 0 ? tg.GetChild(0) as RectTransform : null;
                    float buttonLowerEdge = firstBtn != null ? ScreenRectOf(firstBtn).yMin : gr.yMin;
                    Add("PANELGEO tabBar=" + gr +
                        " tabButtonLowerEdge=" + buttonLowerEdge.ToString("F0") +
                        " (criterion: video top BELOW tabButtonLowerEdge; container edge is NOT the criterion)");
                }
                foreach (KeyValuePair<string, RectTransform> kv in placed)
                {
                    if ((kv.Key != "video" && kv.Key != "status") || kv.Value == null) continue;
                    Rect cr = ScreenRectOf(kv.Value);
                    Add("PANELGEO " + kv.Key + " rect=" + cr +
                        " topGap=" + (pr.yMax - cr.yMax).ToString("F0") +
                        " bottomGap=" + (cr.yMin - pr.yMin).ToString("F0"));
                }
            }
            Flush("results.txt");
            layoutVerifyRunning = false;
        }

        // A 12: there is no separate panel to close and no cloned chrome to
        // hide. HidePlayerTab() above only toggles the 4th tab body's active
        // state; the calendar's own close button closes the whole calendar.

        // --- interactive playback ---

        // User 20:31 #1: 播放 is MERGED into the pause button. One control:
        // idle -> resolve URL + play; playing -> pause; paused -> resume.
        private void OnPlayPauseClicked()
        {
            try
            {
                string currentUrl = urlInput != null ? urlInput.text : inputUrlText;
                // Same URL while playing/paused -> toggle; a NEW URL -> resolve
                // and play it instead (so the merged button can still change video).
                if (player != null && (player.isPlaying || player.isPaused) &&
                    currentUrl == lastPlayedUrlText)
                {
                    TogglePauseResume();
                    return;
                }
                StartPlaybackFromInput();
            }
            catch (Exception e)
            {
                Add("PLAYPAUSE_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void StartPlaybackFromInput()
        {
            try
            {
                string raw = inputUrlText;
                if (urlInput != null) raw = urlInput.text;
                lastUrlText = raw ?? "";
                Add("PLAY_CLICK: url=" + (raw ?? ""));
                if (string.IsNullOrWhiteSpace(raw))
                {
                    ShowStatus("请输入 B 站视频网址");
                    Flush("results.txt");
                    return;
                }
                string bvid = ExtractBvid(raw);
                if (bvid == null)
                {
                    ShowStatus("无法识别 BV 号");
                    Flush("results.txt");
                    return;
                }
                lastPlayedUrlText = raw;
                ShowStatus("解析中…");
                Flush("results.txt");
                // [G / C-CHG-01 / E-M04] Bumping the generation makes any
                // in-flight chain stale before the new one is started. The old
                // chain never writes status / player.url / Play() again.
                resolveGeneration++;
                resolving = true;
                int generation = resolveGeneration;
                Add("RESOLVE: start gen=" + generation + " bvid=" + bvid +
                    " resolving=true (previous chain, if any, is now stale)");
                ModHelper.StartCoroutine(ModId, ResolveAndPlayCo(bvid, generation));
            }
            catch (Exception e)
            {
                resolving = false;
                Add("PLAY_CLICK_FAIL " + e.GetType().Name + ": " + e.Message);
                // [G / E-M05] Never leave the UI stuck on "解析中…"/"加载中…".
                // [G-R6 / C-CHG-02] 5 处失败文案统一为「播放失败」，原因下沉到 PLAY_FAIL 明细行。
                Add("PLAY_FAIL stage=click detail=" + e.GetType().Name + ": " + e.Message);
                ShowStatus("播放失败");
                Flush("results.txt");
            }
        }

        private static string ExtractBvid(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(url, @"BV[0-9A-Za-z]{10}");
            return m.Success ? m.Value : null;
        }

        // [G / E-M04 + E-M05] cid / directUrl are coroutine locals; every
        // await point re-checks the generation; every external-input failure
        // lands on one of the frozen STATUS texts instead of throwing out of
        // the iterator and stranding the UI in "加载中…".
        private IEnumerator ResolveAndPlayCo(string bvid, int generation)
        {
            string localCid = null;
            string localDirectUrl = null;
            prepared = false;
            lastError = null;
            try
            {
                if (IsStaleResolve(generation)) { Add("RESOLVE: stale gen=" + generation + " dropped before start"); yield break; }
                yield return RequestText("https://api.bilibili.com/x/web-interface/view?bvid=" + bvid, 20f,
                    delegate (UnityWebRequest req)
                    {
                        if (!IsStaleResolve(generation)) localCid = ParseViewCid(req);
                    });
                if (IsStaleResolve(generation)) { Add("RESOLVE: stale gen=" + generation + " dropped after view"); yield break; }
                if (string.IsNullOrEmpty(localCid))
                {
                    // [G-R6 / C-CHG-02] stage=view：view 接口未取到 cid。
                    Add("PLAY_FAIL stage=view detail=view-cid-empty gen=" + generation +
                        " bvid=" + bvid);
                    ShowStatus("播放失败");
                    Flush("results.txt");
                    yield break;
                }
                yield return RequestText("https://api.bilibili.com/x/player/playurl?bvid=" + bvid + "&cid=" + localCid + "&qn=32&platform=html5&high_quality=1", 20f,
                    delegate (UnityWebRequest req)
                    {
                        if (!IsStaleResolve(generation)) localDirectUrl = ParsePlayUrlDirect(req);
                    });
                if (IsStaleResolve(generation)) { Add("RESOLVE: stale gen=" + generation + " dropped after playurl"); yield break; }
                if (string.IsNullOrEmpty(localDirectUrl))
                {
                    // [G-R6 / C-CHG-02] stage=playurl：playurl 未取到可播直链。
                    Add("PLAY_FAIL stage=playurl detail=playurl-no-direct-url gen=" + generation +
                        " bvid=" + bvid);
                    ShowStatus("播放失败");
                    Flush("results.txt");
                    yield break;
                }
                if (!TrySetPlayerUrl(localDirectUrl, generation))
                {
                    // [G-R6 / C-CHG-02] stage=seturl：直链被拒或 player 为空。
                    Add("PLAY_FAIL stage=seturl detail=seturl-rejected gen=" + generation +
                        " bvid=" + bvid);
                    ShowStatus("播放失败");
                    Flush("results.txt");
                    yield break;
                }
                ShowStatus("加载中…");
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 20f)
                {
                    if (IsStaleResolve(generation)) { Add("RESOLVE: stale gen=" + generation + " dropped while preparing"); yield break; }
                    // [G-R5 / E2-L05] Only a callback tagged with THIS chain's
                    // generation may end the wait (F-R2 §13.3): a stale chain's
                    // prepareCompleted / errorReceived no longer leaks in.
                    bool chainPrepared = prepared && preparedGen == generation;
                    bool chainError = !string.IsNullOrEmpty(lastError) && lastErrorGen == generation;
                    if (chainPrepared || chainError) break;
                    yield return null;
                }
                if (IsStaleResolve(generation)) { Add("RESOLVE: stale gen=" + generation + " dropped before Play"); yield break; }
                if (!TryPlay(generation))
                {
                    // [G-R6 / C-CHG-02] 最后一个失败出口拆两级：
                    //   stage=prepare —— 播放器报了 errorReceived（lastError 有值）；
                    //   stage=play    —— 未报错但 20 s 内未 prepared（超时）或 Play() 抛异常。
                    bool prepareErrored = !string.IsNullOrEmpty(lastError);
                    Add("PLAY_FAIL stage=" + (prepareErrored ? "prepare" : "play") +
                        " detail=" + (prepareErrored ? lastError : "not-prepared-timeout-or-play-threw") +
                        " gen=" + generation + " bvid=" + bvid);
                    ShowStatus("播放失败");
                    Flush("results.txt");
                    yield break;
                }
                ShowStatus("播放中");
                ShowExternalVideo();
                Uri played;
                string host = IsPlayableHttpUrl(localDirectUrl, out played) ? played.Host : "-";
                Add("PLAYBACK: started bvid=" + bvid + " host=" + host + " gen=" + generation);
                Flush("results.txt");
            }
            finally
            {
                if (generation == resolveGeneration)
                {
                    resolving = false;
                    Add("RESOLVE: end gen=" + generation + " resolving=false");
                    Flush("results.txt");
                }
            }
        }

        private bool TrySetPlayerUrl(string url, int generation)
        {
            try
            {
                Uri parsed;
                if (!IsPlayableHttpUrl(url, out parsed))
                {
                    Add("PLAYURL REJECT: final url validation failed gen=" + generation);
                    return false;
                }
                if (player == null) { Add("PLAY: player null gen=" + generation); return false; }
                prepared = false;
                lastError = null;
                // [G-R5 / E2-L05] Bind the upcoming Prepare / Error callbacks to
                // this chain and drop the previous chain's tags.
                activePrepareGen = generation;
                preparedGen = -1;
                lastErrorGen = -1;
                preparedUrl = null;
                errorUrl = null;
                player.url = url;
                player.Prepare();
                Add("PLAY: prepare requested host=" + parsed.Host + " gen=" + generation);
                return true;
            }
            catch (Exception e)
            {
                Add("PLAY_PREPARE_FAIL " + e.GetType().Name + ": " + e.Message + " gen=" + generation);
                return false;
            }
        }

        private bool TryPlay(int generation)
        {
            try
            {
                if (player == null || !player.isPrepared) { Add("PLAY: not prepared gen=" + generation); return false; }
                player.Play();
                Add("PLAY: Play() ok gen=" + generation);
                return true;
            }
            catch (Exception e)
            {
                Add("PLAY_FAIL " + e.GetType().Name + ": " + e.Message + " gen=" + generation);
                return false;
            }
        }

        private void TogglePauseResume()
        {
            try
            {
                if (player == null) return;
                if (player.isPlaying)
                {
                    player.Pause();
                    if (playPauseText != null) playPauseText.text = "继续";
                    ShowStatus("已暂停");
                    Add("UI: pause -> isPaused=" + player.isPaused + " time=" + player.time.ToString("F2"));
                }
                else if (player.isPaused)
                {
                    player.Play();
                    if (playPauseText != null) playPauseText.text = "暂停";
                    ShowStatus("播放中");
                    Add("UI: resume -> isPaused=" + player.isPaused + " time=" + player.time.ToString("F2"));
                }
                else
                {
                    Add("UI: pause/resume ignored (not playing)");
                }
                Flush("results.txt");
            }
            catch (Exception e)
            {
                Add("PAUSE_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void OnSeekSlider(float v)
        {
            try
            {
                if (player == null || !player.isPrepared || player.length <= 0 || !player.canSetTime) return;
                double target = v * player.length;
                if (Math.Abs(target - player.time) > 0.5)
                {
                    double before = player.time;
                    long frameBefore = player.frame;
                    int seq = ++seekSequence;
                    pendingSeekTarget = target;
                    // Keep the original, stage-B verified setter. Live runs
                    // (20:07 G-R1, 20:28 G-R2) showed the seek DOES take effect
                    // - the next read of player.time lands near the target -
                    // while player.time readback and seekCompleted still report
                    // the PRE-seek value. F §4.2 branch 1: log-format/async
                    // timing issue, not a functional defect. Do NOT add retries
                    // or change the setter; verify with a delayed read instead.
                    player.time = target;
                    Add("UI: seek -> seq=" + seq + " v=" + v.ToString("F2") +
                        " target=" + target.ToString("F2") +
                        " len=" + player.length.ToString("F2") +
                        " timeBefore=" + before.ToString("F2") + " frameBefore=" + frameBefore +
                        " timeAfterSet=" + player.time.ToString("F2") + " frameAfterSet=" + player.frame +
                        " (readback may be pre-seek; see SEEK_VERIFY)");
                    Flush("results.txt");
                    // [G-R5 / E2-L06] Every seek owns a verification coroutine
                    // carrying its own target by value; a second seek inside
                    // 0.6 s can no longer hijack the first one's readback.
                    ModHelper.StartCoroutine(ModId, VerifySeekCo(target, seq));
                }
            }
            catch (Exception e)
            {
                Add("SEEK_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        // [G-R3 / E-H01 / F §4.2] Authoritative seek evidence. On this URL
        // source the setter readback AND seekCompleted both report the
        // PRE-seek value; the decoder applies the seek a few frames later.
        // A delayed real-time readback is the only decidable evidence for
        // C-7 / V-06. This is evidence-only: it never changes playback.
        private IEnumerator VerifySeekCo(double target, int seq)
        {
            // [G-R5 / E2-L06] target is a per-call local (F-R2 §13.3), not the
            // shared pendingSeekTarget field; the in-flight counter keeps the
            // legacy seekVerifyRunning flag meaningful when seeks overlap.
            seekVerifyInFlight++;
            seekVerifyRunning = true;
            try
            {
                // [G-R5 / E2-L06] Two-stage delayed readback. Live run 08:44
                // showed the decoder can apply a seek later than 0.6 s: the
                // mid seek settled at +0.6 s (delta 0.54) while the end seek
                // still read the pre-seek time at +0.6 s and had visibly
                // jumped by +1.3 s. Both readings are logged verbatim; the
                // second one is only taken when the first is still far off,
                // and it never changes playback.
                yield return new WaitForSecondsRealtime(0.6f);
                LogSeekVerify(target, seq, 1, false);
                if (player != null && target >= 0 && Math.Abs(player.time - target) > 1.5)
                {
                    yield return new WaitForSecondsRealtime(1.0f);
                    LogSeekVerify(target, seq, 2, true);
                }
            }
            finally
            {
                // [G-R5 / E2-L06] Always release the flag and persist, even when
                // the readback above throws - otherwise every later seek would
                // silently lose its evidence (E2-L06).
                seekVerifyInFlight--;
                if (seekVerifyInFlight < 0) seekVerifyInFlight = 0;
                seekVerifyRunning = seekVerifyInFlight > 0;
                Flush("results.txt");
            }
        }

        // [G-R5 / E2-L06] Single readback line; attempt=1 is the legacy 0.6 s
        // sample, attempt=2 is the settled sample taken only when the first is
        // still far from the target.
        private void LogSeekVerify(double target, int seq, int attempt, bool settled)
        {
            try
            {
                if (player != null && target >= 0)
                {
                    double actual = player.time;
                    Add("SEEK_VERIFY seq=" + seq + " attempt=" + attempt +
                        " target=" + target.ToString("F2") +
                        " actual=" + actual.ToString("F2") +
                        " delta=" + (actual - target).ToString("F2") +
                        " frame=" + player.frame +
                        (settled ? " (settled readback; authoritative for C-7 / V-06)"
                                 : " (delayed readback; see attempt=2 when delta is large)"));
                }
                else
                {
                    Add("SEEK_VERIFY seq=" + seq + " attempt=" + attempt + " skipped (player/target unavailable)");
                }
            }
            catch (Exception e)
            {
                Add("SEEK_VERIFY_FAIL seq=" + seq + " attempt=" + attempt + " " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void OnVolumeSlider(float v)
        {
            try
            {
                if (audioSource != null) audioSource.volume = v;
                Add("UI: volume -> " + v.ToString("F2") + " audioSource.volume=" + (audioSource != null ? audioSource.volume.ToString("F2") : "-") +
                    " AudioListener.volume=" + AudioListener.volume.ToString("F2"));
            }
            catch (Exception e)
            {
                Add("VOLUME_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void ShowStatus(string msg)
        {
            Add("STATUS: " + msg);
            lastStatusText = msg;
            if (statusLabel != null) statusLabel.text = msg;
            // Keep the merged button's label in sync with playback state.
            if (playPauseText != null)
            {
                if (msg == "播放中") playPauseText.text = "暂停";
                else if (msg == "已暂停") playPauseText.text = "继续";
                else if (msg == "就绪" || msg.IndexOf("失败") >= 0 ||
                         msg.IndexOf("无法") >= 0 || msg.IndexOf("请输入") >= 0)
                {
                    playPauseText.text = "播放";
                }
            }
        }

        private IEnumerator ProgressLoop()
        {
            progressLoopRunning = true;
            float lastFlush = Time.realtimeSinceStartup;
            while (playerTabActive)
            {
                try
                {
                    if (player != null && player.isPrepared && player.length > 0)
                    {
                        float p = (float)(player.time / player.length);
                        // SetValueWithoutNotify avoids feeding our own progress
                        // update back into OnSeekSlider (would restart seeking).
                        if (progressSlider != null) progressSlider.SetValueWithoutNotify(p);
                    }
                    if (player != null)
                    {
                        lastTimeText = FormatTime(player.time) + " / " + FormatTime(player.length);
                        if (timeLabel != null) timeLabel.text = lastTimeText;
                    }
                }
                catch (Exception e)
                {
                    Add("PROGRESS_FAIL " + e.GetType().Name + ": " + e.Message);
                }
                if (Time.realtimeSinceStartup - lastFlush > 2f)
                {
                    Flush("results.txt");
                    lastFlush = Time.realtimeSinceStartup;
                }
                yield return null;
            }
            progressLoopRunning = false;
            Add("PROGRESS: loop exit");
            Flush("results.txt");
        }

        private static string FormatTime(double s)
        {
            if (double.IsNaN(s) || double.IsInfinity(s) || s < 0) s = 0;
            int total = (int)s;
            int mm = total / 60;
            int ss = total % 60;
            return mm.ToString("00") + ":" + ss.ToString("00");
        }

        // Stage B framebuffer capture (40 PNGs into ModData) and MANUAL
        // auto-test prompts were removed in stage D: the product writes no
        // image/video files and never drives playback automatically.

        private void LogTabLayout()
        {
            try
            {
                if (playerTabButton == null) { Add("TABCHK: tab button missing"); return; }
                RectTransform tr = (RectTransform)playerTabButton.transform;
                Add("TABCHK: tabRect=" + ScreenRectOf(tr) +
                    " size=" + tr.rect.width.ToString("F0") + "x" + tr.rect.height.ToString("F0") +
                    " content=" + (panelRoot != null ? panelRoot.name : "null") +
                    " hooks=" + tabHooksAdded);
                Add("TABCHK: A-1/A-8 intact - StoreCalendar.OnClick still opens the ORIGINAL calendar");
                Add("TABCHK: no standalone button, no cloned window, no overlay canvas");
            }
            catch (Exception e)
            {
                Add("TABCHK_FAIL " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static Rect ScreenRectOf(RectTransform r)
        {
            Vector3[] corners = new Vector3[4];
            r.GetWorldCorners(corners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private void Add(string line)
        {
            report.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line);
            try { log.Log(line); } catch { }
        }

        private void Flush(string fileName = "results.txt")
        {
            try { ModHelper.WriteModDataFileText(ModId, fileName, report.ToString()); }
            catch (Exception e) { try { log.Error("write failed: " + e.Message); } catch { } }
        }

        private void Cleanup()
        {
            try
            {
                leftSave = true;          // stop the SaveLifecycleGuard loop
                playerTabActive = false;
                CancelInFlightResolve("Cleanup/OnDisable");
                if (player != null)
                {
                    player.Stop();
                    // [G / E-L07] Explicitly unsubscribe, same as LeaveSaveCleanup's
                    // reference hygiene; the host GO is destroyed below.
                    player.prepareCompleted -= OnPrepared;
                    player.started -= OnStarted;
                    player.errorReceived -= OnError;
                    player.seekCompleted -= OnSeekCompleted;
                }
                RestoreExternalDisplay("CLEANUP:OnDisable");
                if (externalOverlayGo != null) { UnityEngine.Object.Destroy(externalOverlayGo); externalOverlayGo = null; }
                // Undo the runtime-only additions to the ORIGINAL calendar UI.
                if (calendar != null && tabHooksAdded)
                {
                    if (calendar.calendarButton != null) calendar.calendarButton.onClick.RemoveListener(HidePlayerTab);
                    if (calendar.nightlyReportButton != null) calendar.nightlyReportButton.onClick.RemoveListener(HidePlayerTab);
                    if (calendar.statusButton != null) calendar.statusButton.onClick.RemoveListener(HidePlayerTab);
                    tabHooksAdded = false;
                }
                if (calendarWatcher != null)
                {
                    calendarWatcher.onOpen = null;
                    calendarWatcher.onClose = null;
                    UnityEngine.Object.Destroy(calendarWatcher);
                    calendarWatcher = null;
                }
                // Restore the stock tab-bar geometry we adjusted for centring.
                if (tabGeometrySaved && calendar != null && calendar.UIPanel != null)
                {
                    Transform tg = calendar.UIPanel.transform.Find("TopToggleGroup");
                    if (tg != null)
                    {
                        HorizontalLayoutGroup hlg = tg.GetComponent<HorizontalLayoutGroup>();
                        if (hlg != null) hlg.childAlignment = origTabAlignment;
                        RectTransform trt = (RectTransform)tg;
                        trt.sizeDelta = origTabSize;
                        trt.anchoredPosition = origTabPos;
                    }
                    tabGeometrySaved = false;
                }
                // [G-R5 / E2-M03 / F-R2 §13.2] Pre-uninstall baseline in the SAME
                // format as the post-restore snapshot, so V-11 can be compared
                // line by line.
                LogCleanupTabState("before");
                // [G-R5 / E2-M03] Back to the regular deferred Destroy: evidence
                // gathering must never change product runtime behaviour (F-R2
                // §13.2 overrules the G-R4 immediate-destroy workaround). The
                // authoritative post-restore snapshot is taken on the NEXT
                // frame, which is reachable on the in-game "disable mod" path
                // (process alive) required by V-11.
                if (playerTabButton != null) { UnityEngine.Object.Destroy(playerTabButton); playerTabButton = null; }
                if (panelRoot != null) { UnityEngine.Object.Destroy(panelRoot); panelRoot = null; }
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
                if (playerGo != null) { UnityEngine.Object.Destroy(playerGo); playerGo = null; }
                // [G / E-L07] Drop stale component references after destroying
                // the host, matching LeaveSaveCleanup's cleanup discipline.
                player = null; audioSource = null; screen = null;
                urlInput = null; statusLabel = null; timeLabel = null; playPauseText = null;
                progressSlider = null; volumeSlider = null;
                pendingSeekTarget = -1.0;
                Add("CLEANUP: tab hooks removed=" + !tabHooksAdded +
                    " tabGeometryRestored=" + !tabGeometrySaved +
                    " dayTMPHidden=" + dayTmpHidden + " rtReleased=" + (rt == null) +
                    " playerRefsDropped=true");
                // [G-R5 / E2-M03] No same-frame post-restore sample any more:
                // Destroy() only takes effect at end of frame, so the same-frame
                // numbers would describe the pre-restore state (the D-stage
                // contradiction). The next-frame coroutine below is the single
                // authoritative measurement point.
                Add("CLEANUP SNAPSHOT: scheduled for nextFrame (deferred Destroy; no immediate destroy)");
                ModHelper.StartCoroutine(ModId, LogCleanupSnapshotNextFrame());
                Flush("results.txt");
            }
            catch (Exception e)
            {
                Add("CLEANUP_FAIL " + e.GetType().Name + ": " + e.Message);
                Flush("results.txt");
            }
        }

        // [G / E-M03] Restoration evidence, taken after Destroy actually ran.
        // This is what V-11 / C-2 / C-12 compare against the pre-uninstall HDR.
        private IEnumerator LogCleanupSnapshotNextFrame()
        {
            yield return null;
            try
            {
                Add("CLEANUP SNAPSHOT: nextFrame begin (Destroy has taken effect)");
                LogCleanupTabState("nextFrame");
                Add("CLEANUP SNAPSHOT: nextFrame end");
                Flush("results.txt");
            }
            catch (Exception e)
            {
                Add("CLEANUP_SNAPSHOT_FAIL " + e.GetType().Name + ": " + e.Message);
                Flush("results.txt");
            }
        }

        // [G-R5 / E2-M03] Same-format tab-state sampler, used for BOTH the
        // pre-uninstall baseline and the post-restore next-frame snapshot so
        // V-11 can be compared line by line (F-R2 §13.2).
        private void LogCleanupTabState(string phase)
        {
            try
            {
                if (calendar == null || calendar.UIPanel == null)
                {
                    Add("CLEANUP TABGEO (" + phase + ") skipped - calendar/UIPanel unavailable");
                    Add("CLEANUP RESIDUE (" + phase + ") skipped - calendar/UIPanel unavailable");
                    return;
                }
                Transform tg = calendar.UIPanel.transform.Find("TopToggleGroup");
                if (tg != null)
                {
                    HorizontalLayoutGroup hlg = tg.GetComponent<HorizontalLayoutGroup>();
                    Add("CLEANUP TABGEO (" + phase + ") children=" + tg.childCount +
                        " align=" + (hlg != null ? hlg.childAlignment.ToString() : "null") +
                        " size=" + ((RectTransform)tg).sizeDelta +
                        " pos=" + ((RectTransform)tg).anchoredPosition);
                }
                else
                {
                    Add("CLEANUP TABGEO (" + phase + "): TopToggleGroup not found");
                }
                int tabButtons, contents, dspNodes, watchers;
                CountCleanupResidue(out tabButtons, out contents, out dspNodes, out watchers);
                Add("CLEANUP RESIDUE (" + phase + ") PlayerTabButton=" + tabButtons +
                    " DspPlayerTabContent=" + contents +
                    " dsp_*=" + dspNodes +
                    " CalendarTabWatcher=" + watchers +
                    " total=" + (tabButtons + contents + dspNodes + watchers));
                Add("CLEANUP HDR (" + phase + "): hierarchy follows");
                DumpHierarchy(calendar.UIPanel.transform, 0);
            }
            catch (Exception e)
            {
                Add("CLEANUP_STATE_FAIL (" + phase + ") " + e.GetType().Name + ": " + e.Message);
            }
        }

        // [G-R5 / E2-M03] Counts every residue the mod could leave on the
        // ORIGINAL calendar panel. F-R2 §13.2 requires all four to be 0 after
        // restoration, including CalendarTabWatcher, which E-R2 found still
        // listed in the G-R4 same-frame sample.
        private void CountCleanupResidue(out int tabButtons, out int contents, out int dspNodes, out int watchers)
        {
            tabButtons = 0; contents = 0; dspNodes = 0; watchers = 0;
            if (calendar == null || calendar.UIPanel == null) return;
            Transform root = calendar.UIPanel.transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == "PlayerTabButton") tabButtons++;
                else if (n == "DspPlayerTabContent") contents++;
                else if (n.StartsWith("dsp_", StringComparison.Ordinal)) dspNodes++;
            }
            watchers = root.GetComponentsInChildren<CalendarTabWatcher>(true).Length;
        }
    }

    // Attached to the ORIGINAL AdvCalendarUIManager.UIPanel so opening or
    // closing the calendar also leaves the 4th player tab (C 4.5 tier 1: hide tab body only, playback keeps running). Pure
    // callback holder: no stock component is patched or replaced.
    public class CalendarTabWatcher : MonoBehaviour
    {
        public Action onOpen;
        public Action onClose;

        private void OnEnable()
        {
            if (onOpen != null) onOpen();
        }

        private void OnDisable()
        {
            if (onClose != null) onClose();
        }
    }

    // Reused from the stage-B legacy probe (verified 12:30 / 13:07 runs):
    // draws a texture onto an arbitrary tilted quad so video covers the whole
    // yellow calendar screen, not just the dayTMP text rectangle.
    public class WarpedTextureGraphic : MaskableGraphic
    {
        private Texture sourceTexture;
        private readonly Vector2[] quad = new Vector2[4];
        private bool hasQuad;

        public override Texture mainTexture
        {
            get { return sourceTexture != null ? sourceTexture : Texture2D.whiteTexture; }
        }

        public void SetQuad(Vector2 bl, Vector2 tl, Vector2 tr, Vector2 br, Texture tex)
        {
            quad[0] = bl;
            quad[1] = tl;
            quad[2] = tr;
            quad[3] = br;
            sourceTexture = tex;
            hasQuad = true;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!hasQuad) return;
            Color32 c = color;
            vh.AddVert(quad[0], c, new Vector2(0f, 0f));
            vh.AddVert(quad[1], c, new Vector2(0f, 1f));
            vh.AddVert(quad[2], c, new Vector2(1f, 1f));
            vh.AddVert(quad[3], c, new Vector2(1f, 0f));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
