/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

#if UNITY_IOS
using Firebase;
using Firebase.Extensions;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Web;
using Cgs.Menu;
using FinolDigital.Cgs.CardGameDef;
using FinolDigital.Cgs.CardGameDef.Unity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityExtensionMethods;
#if UNITY_ANDROID || UNITY_IOS
using Firebase.DynamicLinks;
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

[assembly: InternalsVisibleTo("PlayMode")]

namespace Cgs
{
    public class CardGameManager : MonoBehaviour
    {
        // Show all Debug.Log() to help with debugging?
        private const bool IsMessengerDebugLogVerbose = false;
        public const string PlayerPrefsDefaultGame = "DefaultGame";
        public const string DefaultNameWarning = "Found game with default name. Deleting it.";
        public const string SelectionErrorMessage = "Could not select the card game because it is not recognized!: ";
        public const string DownloadErrorMessage = "Error downloading game!: ";
        public const string LoadErrorMessage = "Error loading game!: ";

        public const string FileNotFoundErrorMessage = "ERROR: File Not Found at {0}";
        public const string OverwriteGamePrompt = "Game already exists. Overwrite?";
        public const string ImportFailureErrorMessage = "ERROR: Failed to Import! ";

        public const string LoadErrorPrompt =
            "Error loading game! The game may be corrupted. RequestDelete (note that any decks would also be deleted)?";

        public const string CardsLoadedMessage = "{0} cards loaded!";
        public const string CardsLoadingMessage = "{0} cards loading...";
        public const string SetCardsLoadedMessage = "{0} set cards loaded!";
        public const string SetCardsLoadingMessage = "{0} set cards loading...";
        public const string DeleteErrorMessage = "Error deleting game!: ";
        public const string DeleteWarningMessage = "Please download additional card games before deleting.";

        public const string DeletePrompt =
            "Deleting a card game also deletes all decks saved for that card game. Are you sure you would like to delete this card game?";

        public const string ShareDeepLinkMessage = "Get CGS for {0}: {1}";

        public const int CardsLoadingMessageThreshold = 60;
        public const int PixelsPerInch = 100;

        public static CardGameManager Instance
        {
            get
            {
                if (IsQuitting)
                    return null;
                if (_instance == null)
                    _instance = GameObject.FindGameObjectWithTag(Tags.CardGameManager).GetComponent<CardGameManager>();
                return _instance;
            }
            private set => _instance = value;
        }

        private static CardGameManager _instance;

        public static UnityCardGame Current { get; private set; } = UnityCardGame.UnityInvalid;
        public static bool IsQuitting { get; private set; }

        public bool IsSearchingForServer { get; set; }

        public SortedDictionary<string, UnityCardGame> AllCardGames { get; } = new();

        public UnityCardGame Previous
        {
            get
            {
                var previousCardGame = AllCardGames.Values.LastOrDefault() ?? UnityCardGame.UnityInvalid;

                using var allCardGamesEnumerator = AllCardGames.GetEnumerator();
                var found = false;
                while (!found && allCardGamesEnumerator.MoveNext())
                {
                    if (allCardGamesEnumerator.Current.Value != Current)
                        previousCardGame = allCardGamesEnumerator.Current.Value;
                    else
                        found = true;
                }

                return previousCardGame;
            }
        }

        public UnityCardGame Next
        {
            get
            {
                var nextCardGame = AllCardGames.Values.FirstOrDefault() ?? UnityCardGame.UnityInvalid;

                using var allCardGamesEnumerator = AllCardGames.GetEnumerator();
                var found = false;
                while (!found && allCardGamesEnumerator.MoveNext())
                    if (allCardGamesEnumerator.Current.Value == Current)
                        found = true;
                if (allCardGamesEnumerator.MoveNext())
                    nextCardGame = allCardGamesEnumerator.Current.Value;

                return nextCardGame;
            }
        }

        public HashSet<UnityAction> OnSceneActions { get; } = new();

        public HashSet<Canvas> CardCanvases { get; } = new();

        public Canvas CardCanvas
        {
            get
            {
                Canvas topCanvas = null;
                CardCanvases.RemoveWhere((canvas) => canvas == null);
                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (var canvas in CardCanvases)
                    if (canvas.gameObject.activeSelf &&
                        (topCanvas == null || canvas.sortingOrder > topCanvas.sortingOrder))
                        topCanvas = canvas;
                return topCanvas;
            }
        }

        public HashSet<Canvas> ModalCanvases { get; } = new();

        public Canvas ModalCanvas
        {
            get
            {
                Canvas topCanvas = null;
                ModalCanvases.RemoveWhere((canvas) => canvas == null);
                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (var canvas in ModalCanvases)
                    if (canvas.gameObject.activeSelf &&
                        (topCanvas == null || canvas.sortingOrder > topCanvas.sortingOrder))
                        topCanvas = canvas;
                return topCanvas;
            }
        }

        public Dialog Messenger => messenger;

        public Dialog messenger;

        public ProgressBar Progress => progress;

        public ProgressBar progress;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            UnityCardGame.UnityInvalid.CoroutineRunner = this;
            DontDestroyOnLoad(gameObject);

            if (!Directory.Exists(UnityCardGame.GamesDirectoryPath))
                CreateDefaultCardGames();
            LookupCardGames();

            if (Debug.isDebugBuild)
                Application.logMessageReceived += ShowLogToUser;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            ResetCurrentToDefault();

            Debug.Log("CardGameManager is Awake!");
        }

        private void Start()
        {
#if !UNITY_WEBGL
            Debug.Log("CardGameManager::Start:CheckDeepLinks");
            CheckDeepLinks();
#endif
        }

        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void CreateDefaultCardGames()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityFileMethods.ExtractAndroidStreamingAssets(UnityCardGame.GamesDirectoryPath);
#elif UNITY_WEBGL
            if (!Directory.Exists(UnityCardGame.GamesDirectoryPath))
                Directory.CreateDirectory(UnityCardGame.GamesDirectoryPath);
            string standardPlayingCardsDirectory =
                UnityCardGame.GamesDirectoryPath + "/" + Tags.StandardPlayingCardsDirectoryName;
            if (!Directory.Exists(standardPlayingCardsDirectory))
                Directory.CreateDirectory(standardPlayingCardsDirectory);
            File.WriteAllText(standardPlayingCardsDirectory + "/" + Tags.StandardPlayingCardsJsonFileName,
                Tags.StandPlayingCardsJsonFileContent);
            string dominoesDirectory = UnityCardGame.GamesDirectoryPath + "/" + Tags.DominoesDirectoryName;
            if (!Directory.Exists(dominoesDirectory))
                Directory.CreateDirectory(dominoesDirectory);
            File.WriteAllText(dominoesDirectory + "/" + Tags.DominoesJsonFileName, Tags.DominoesJsonFileContent);
            StartCoroutine(
                UnityFileMethods.SaveUrlToFile(Tags.DominoesCardBackUrl, dominoesDirectory + "/CardBack.png"));
            string mahjongDirectory = UnityCardGame.GamesDirectoryPath + "/" + Tags.MahjongDirectoryName;
            if (!Directory.Exists(mahjongDirectory))
                Directory.CreateDirectory(mahjongDirectory);
            File.WriteAllText(mahjongDirectory + "/" + Tags.MahjongJsonFileName, Tags.MahjongJsonFileContent);
            StartCoroutine(UnityFileMethods.SaveUrlToFile(Tags.MahjongCardBackUrl, mahjongDirectory + "/CardBack.png"));
#else
            UnityFileMethods.CopyDirectory(Application.streamingAssetsPath, UnityCardGame.GamesDirectoryPath);
#endif
        }

        internal void LookupCardGames()
        {
            if (!Directory.Exists(UnityCardGame.GamesDirectoryPath) ||
                Directory.GetDirectories(UnityCardGame.GamesDirectoryPath).Length < 1)
                CreateDefaultCardGames();

            foreach (var gameDirectory in Directory.GetDirectories(UnityCardGame.GamesDirectoryPath))
            {
                var gameDirectoryName = gameDirectory[(UnityCardGame.GamesDirectoryPath.Length + 1)..];
                var (gameName, _) = CardGame.GetNameAndHost(gameDirectoryName);
                if (gameName.Equals(CardGame.DefaultName))
                {
                    Debug.LogWarning(DefaultNameWarning);
                    try
                    {
                        Directory.Delete(gameDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(DeleteErrorMessage + ex.Message);
                    }
                }
                else
                {
                    var newCardGame = new UnityCardGame(this, gameDirectoryName);
                    newCardGame.ReadProperties();
                    if (!string.IsNullOrEmpty(newCardGame.Error))
                        Debug.LogError(LoadErrorMessage + newCardGame.Error);
                    else
                        AllCardGames[newCardGame.Id] = newCardGame;
                }
            }
        }

        public void ImportCardGame(string zipFilePath)
        {
            if (!File.Exists(zipFilePath))
            {
                var errorMessage = string.Format(FileNotFoundErrorMessage, zipFilePath);
                Debug.LogError(errorMessage);
                Messenger.Show(errorMessage);
                return;
            }

            var gameId = Path.GetFileNameWithoutExtension(zipFilePath);
            var targetGameDirectory = Path.Combine(UnityCardGame.GamesDirectoryPath, gameId);
            if (File.Exists(targetGameDirectory))
            {
                Messenger.Ask(OverwriteGamePrompt, () => { },
                    () => ForceImportCardGame(zipFilePath));
                return;
            }

            ForceImportCardGame(zipFilePath);
        }

        private void ForceImportCardGame(string zipFilePath)
        {
            var gameId = Path.GetFileNameWithoutExtension(zipFilePath);
            if (string.IsNullOrEmpty(gameId))
            {
                var errorMessage = string.Format(FileNotFoundErrorMessage, zipFilePath);
                Debug.LogError(errorMessage);
                Messenger.Show(errorMessage);
                return;
            }

            try
            {
                UnityFileMethods.ExtractZip(zipFilePath, UnityCardGame.GamesImportPath);

                var importGameDirectory = Path.Combine(UnityCardGame.GamesImportPath, gameId);
                if (!Directory.Exists(importGameDirectory))
                {
                    gameId = Path.GetFileName(Directory.GetDirectories(UnityCardGame.GamesImportPath)[0]);
                    importGameDirectory = Path.Combine(UnityCardGame.GamesImportPath, gameId);
                    if (!Directory.Exists(importGameDirectory))
                    {
                        var errorMessage = string.Format(FileNotFoundErrorMessage, importGameDirectory);
                        Debug.LogError(errorMessage);
                        Messenger.Show(errorMessage);
                        return;
                    }
                }

                var targetGameDirectory = Path.Combine(UnityCardGame.GamesDirectoryPath, gameId);
                if (Directory.Exists(targetGameDirectory))
                    Directory.Delete(targetGameDirectory, true);

                UnityFileMethods.CopyDirectory(importGameDirectory, targetGameDirectory);

                var newCardGame = new UnityCardGame(this, gameId);
                newCardGame.ReadProperties();
                if (!string.IsNullOrEmpty(newCardGame.Error))
                {
                    Debug.LogError(LoadErrorMessage + newCardGame.Error);
                    Messenger.Show(LoadErrorMessage + newCardGame.Error);
                }
                else
                {
                    AllCardGames[newCardGame.Id] = newCardGame;
                    Select(newCardGame.Id);
                }

                if (Directory.Exists(importGameDirectory))
                    Directory.Delete(importGameDirectory, true);
            }
            catch (Exception e)
            {
                Debug.LogError(ImportFailureErrorMessage + e);
                Messenger.Show(ImportFailureErrorMessage + e);
            }
        }

        private void ShowLogToUser(string logString, string stackTrace, LogType type)
        {
            // ReSharper disable once RedundantLogicalConditionalExpressionOperand
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (this != null && Messenger != null && (IsMessengerDebugLogVerbose || !LogType.Log.Equals(type)))
                Messenger.Show(logString);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ResolutionManager.ScaleResolution();
            ResetGameScene();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            OnSceneActions.Clear();
        }

        private void CheckDeepLinks()
        {
            Debug.Log("Checking Deep Links...");
#if UNITY_IOS
            Debug.Log("Should use Firebase Dynamic Links for iOS...");
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                var dependencyStatus = task.Result;
                if (dependencyStatus != DependencyStatus.Available)
                {
                    Debug.LogError("Error with Links! Could not resolve all Firebase dependencies: " + dependencyStatus);
                    Messenger.Show("Error with Links! Could not resolve all Firebase dependencies: " + dependencyStatus);
                    return;
                }

                DynamicLinks.DynamicLinkReceived += OnDynamicLinkReceived;
                Debug.Log("Using Firebase Dynamic Links for iOS!");
            });
            return;
#elif UNITY_ANDROID
            if (string.IsNullOrEmpty(Application.absoluteURL))
            {
                DynamicLinks.DynamicLinkReceived += OnDynamicLinkReceived;
                Debug.Log("Using Firebase Dynamic Links for Android!");
            }
#else
            Application.deepLinkActivated += OnDeepLinkActivated;
            Debug.Log("Using Native Deep Links!");
#endif

            if (string.IsNullOrEmpty(Application.absoluteURL))
            {
                Debug.Log("No Start Deep Link");
                return;
            }

            if (!Uri.IsWellFormedUriString(Application.absoluteURL, UriKind.RelativeOrAbsolute))
            {
                Debug.LogWarning("Start Deep Link malformed: " + Application.absoluteURL);
                return;
            }

            Debug.Log("Start Deep Link: " + Application.absoluteURL);
            OnDeepLinkActivated(Application.absoluteURL);
        }

#if UNITY_ANDROID || UNITY_IOS
        private void OnDynamicLinkReceived(object sender, EventArgs args)
        {
            Debug.Log("OnDynamicLinkReceived!");
            var dynamicLinkEventArgs = args as ReceivedDynamicLinkEventArgs;
            var deepLink = dynamicLinkEventArgs?.ReceivedDynamicLink.Url.OriginalString;
            if (string.IsNullOrEmpty(deepLink))
            {
                Debug.LogError("OnDynamicLinkReceived::deepLinkEmpty");
                Messenger.Show("OnDynamicLinkReceived::deepLinkEmpty");
            }
            else
                OnDeepLinkActivated(deepLink);
        }
#endif

        private void OnDeepLinkActivated(string deepLink)
        {
            Debug.Log("OnDeepLinkActivated!");
            var autoUpdateUrl = GetAutoUpdateUrl(deepLink);
            if (string.IsNullOrEmpty(autoUpdateUrl) ||
                !Uri.IsWellFormedUriString(autoUpdateUrl, UriKind.RelativeOrAbsolute))
            {
                Debug.LogError("OnDeepLinkActivated::autoUpdateUrlMalformed: " + deepLink);
                Messenger.Show("OnDeepLinkActivated::autoUpdateUrlMalformed: " + deepLink);
            }
            else
                StartCoroutine(GetCardGame(autoUpdateUrl));
        }

        private static string GetAutoUpdateUrl(string deepLink)
        {
            Debug.Log("GetAutoUpdateUrl::deepLink: " + deepLink);
            if (string.IsNullOrEmpty(deepLink) || !Uri.IsWellFormedUriString(deepLink, UriKind.RelativeOrAbsolute))
            {
                Debug.LogWarning("GetAutoUpdateUrl::deepLinkMalformed: " + deepLink);
                return null;
            }

            if (deepLink.StartsWith(Tags.DynamicLinkUriDomain))
            {
                var dynamicLinkUri = new Uri(deepLink);
                deepLink = HttpUtility.UrlDecode(HttpUtility.ParseQueryString(dynamicLinkUri.Query).Get("link"));
                Debug.Log("GetAutoUpdateUrl::dynamicLink: " + deepLink);
                if (string.IsNullOrEmpty(deepLink) || !Uri.IsWellFormedUriString(deepLink, UriKind.RelativeOrAbsolute))
                {
                    Debug.LogWarning("GetAutoUpdateUrl::dynamicLinkMalformed: " + deepLink);
                    return null;
                }
            }

            var deepLinkDecoded = HttpUtility.UrlDecode(deepLink);
            Debug.Log("GetAutoUpdateUrl::deepLinkDecoded: " + deepLinkDecoded);
            var deepLinkUriQuery = new Uri(deepLinkDecoded).Query;
            Debug.Log("GetAutoUpdateUrl::deepLinkUriQuery: " + deepLinkUriQuery);
            var autoUpdateUrl = HttpUtility.ParseQueryString(deepLinkUriQuery).Get("url");
            Debug.Log("GetAutoUpdateUrl::autoUpdateUrl: " + autoUpdateUrl);

            return autoUpdateUrl;
        }

        // Note: Does NOT Reset Game Scene
        internal void ResetCurrentToDefault()
        {
            var preferredGameId =
                PlayerPrefs.GetString(PlayerPrefsDefaultGame, Tags.StandardPlayingCardsDirectoryName);
            Current = AllCardGames.TryGetValue(preferredGameId, out var currentGame) &&
                      string.IsNullOrEmpty(currentGame.Error)
                ? currentGame
                : (AllCardGames.FirstOrDefault().Value ?? UnityCardGame.UnityInvalid);
        }

        public IEnumerator GetCardGame(string gameUrl)
        {
            if (string.IsNullOrEmpty(gameUrl))
            {
                Debug.LogError("ERROR: GetCardGame has gameUrl missing!");
                Messenger.Show("ERROR: GetCardGame has gameUrl missing!");
                yield break;
            }

            Debug.Log("GetCardGame: Starting...");
            // If user attempts to download a game they already have, we should just update that game
            UnityCardGame existingGame = null;
            foreach (var cardGame in AllCardGames.Values.Where(cardGame => cardGame != null &&
                                                                           cardGame.AutoUpdateUrl != null &&
                                                                           cardGame.AutoUpdateUrl.Equals(
                                                                               new Uri(gameUrl))))
                existingGame = cardGame;
            Debug.Log("GetCardGame: Existing game search complete...");
            if (existingGame != null)
            {
                Debug.Log("GetCardGame: Existing game found; updating...");
                yield return UpdateCardGame(existingGame);
                Debug.Log("GetCardGame: Existing game found; updated!");
                if (string.IsNullOrEmpty(existingGame.Error))
                    Select(existingGame.Id);
                else
                    Debug.LogError("GetCardGame: Not selecting card game because of an error after update!");
            }
            else
                yield return DownloadCardGame(gameUrl);

            Debug.Log("GetCardGame: Done!");
        }

        private IEnumerator DownloadCardGame(string gameUrl)
        {
            Debug.Log("DownloadCardGame: start");
            var cardGame = new UnityCardGame(this, CardGame.DefaultName, gameUrl);

            Progress.Show(cardGame);
            yield return cardGame.Download();
            Progress.Hide();

            cardGame.Load(UpdateCardGame, LoadCards, LoadSetCards);

            if (!string.IsNullOrEmpty(cardGame.Error))
            {
                Debug.LogError(DownloadErrorMessage + cardGame.Error);
                Messenger.Show(DownloadErrorMessage + cardGame.Error);

                if (!Directory.Exists(cardGame.GameDirectoryPath))
                    yield break;

                try
                {
                    Directory.Delete(cardGame.GameDirectoryPath, true);
                }
                catch (Exception ex)
                {
                    Debug.LogError(DeleteErrorMessage + ex.Message);
                }
            }
            else
            {
                AllCardGames[cardGame.Id] = cardGame;
                Select(cardGame.Id);
            }

            Debug.Log("DownloadCardGame: end");
        }

        public IEnumerator UpdateCardGame(UnityCardGame cardGame)
        {
            cardGame ??= Current;

            Progress.Show(cardGame);
            yield return cardGame.Download(true);
            Progress.Hide();

            // Notify about the failed update, but otherwise ignore errors
            if (!string.IsNullOrEmpty(cardGame.Error))
            {
                Debug.LogError(DownloadErrorMessage + cardGame.Error);
                Messenger.Show(DownloadErrorMessage + cardGame.Error);
                cardGame.ClearError();
            }

            cardGame.Load(UpdateCardGame, LoadCards, LoadSetCards);
            if (cardGame == Current)
                ResetGameScene();
        }

        private IEnumerator LoadCards(UnityCardGame cardGame)
        {
            cardGame ??= Current;

            for (var page = cardGame.AllCardsUrlPageCountStartIndex;
                 page < cardGame.AllCardsUrlPageCountStartIndex + cardGame.AllCardsUrlPageCount;
                 page++)
            {
                cardGame.LoadCards(page);
                if (page == cardGame.AllCardsUrlPageCountStartIndex &&
                    cardGame.AllCardsUrlPageCount > CardsLoadingMessageThreshold)
                    Messenger.Show(string.Format(CardsLoadingMessage, cardGame.Name));
                yield return null;
            }

            if (!string.IsNullOrEmpty(cardGame.Error))
                Debug.LogError(LoadErrorMessage + cardGame.Error);
            else if (cardGame.AllCardsUrlPageCount > CardsLoadingMessageThreshold)
                Messenger.Show(string.Format(CardsLoadedMessage, cardGame.Name));
        }

        private IEnumerator LoadSetCards(UnityCardGame cardGame)
        {
            cardGame ??= Current;

            var setCardsLoaded = false;
            foreach (var set in cardGame.Sets.Values)
            {
                if (string.IsNullOrEmpty(set.CardsUrl))
                    continue;
                if (!setCardsLoaded)
                    Messenger.Show(string.Format(SetCardsLoadingMessage, cardGame.Name));
                setCardsLoaded = true;
                var setCardsFilePath = Path.Combine(cardGame.SetsDirectoryPath,
                    UnityFileMethods.GetSafeFileName(set.Code + UnityFileMethods.JsonExtension));
                if (!File.Exists(setCardsFilePath))
                    yield return UnityFileMethods.SaveUrlToFile(set.CardsUrl, setCardsFilePath);
                if (File.Exists(setCardsFilePath))
                    cardGame.LoadCards(setCardsFilePath, set.Code);
                else
                {
                    Debug.LogError(LoadErrorMessage + set.CardsUrl);
                    yield break;
                }
            }

            if (!string.IsNullOrEmpty(cardGame.Error))
                Debug.LogError(LoadErrorMessage + cardGame.Error);
            else if (setCardsLoaded)
                Messenger.Show(string.Format(SetCardsLoadedMessage, cardGame.Name));
        }

        public void Select(string gameId)
        {
            if (string.IsNullOrEmpty(gameId) || !AllCardGames.ContainsKey(gameId))
            {
                Debug.LogError(SelectionErrorMessage + gameId);
                Messenger.Show(SelectionErrorMessage + gameId);
                return;
            }

            Current = AllCardGames[gameId];
            ResetGameScene();
        }

        internal void ResetGameScene()
        {
            if (!Current.HasLoaded)
            {
                Current.Load(UpdateCardGame, LoadCards, LoadSetCards);
                if (Current.IsDownloading)
                    return;
            }

            if (!string.IsNullOrEmpty(Current.Error))
            {
                Debug.LogError(LoadErrorMessage + Current.Error);
                Messenger.Ask(LoadErrorPrompt, IgnoreCurrentErroredGame, Delete);
                return;
            }

#if UNITY_WEBGL
            foreach (var game in AllCardGames.Values)
                game.ReadProperties();
#endif

            // Now is the safest time to set this game as the preferred default game for the player
            PlayerPrefs.SetString(PlayerPrefsDefaultGame, Current.Id);

            // Each scene is responsible for adding to OnSceneActions, but they may not remove
            OnSceneActions.RemoveWhere((action) => action == null);
            foreach (var action in OnSceneActions)
                action();
        }

        private void IgnoreCurrentErroredGame()
        {
            Current.ClearError();
            ResetCurrentToDefault();
            ResetGameScene();
        }

        public void PromptDelete()
        {
            if (AllCardGames.Count > 1)
                Messenger.Prompt(DeletePrompt, Delete);
            else
                Messenger.Show(DeleteWarningMessage);
        }

        private void Delete()
        {
            if (AllCardGames.Count < 1)
            {
                Debug.LogError(DeleteErrorMessage + DeleteWarningMessage);
                return;
            }

            try
            {
                Directory.Delete(Current.GameDirectoryPath, true);
                AllCardGames.Remove(Current.Id);
                ResetCurrentToDefault();
                ResetGameScene();
            }
            catch (Exception ex)
            {
                Debug.LogError(DeleteErrorMessage + ex.Message);
            }
        }

        public void Share()
        {
            Debug.Log("CGS Share:: Deep:" + Current.CgsGamesLink + " Auto:" + Current.AutoUpdateUrl);
            if (Current.AutoUpdateUrl != null && Current.AutoUpdateUrl.IsWellFormedOriginalString())
            {
                var deepLink = Current.CgsGamesLink?.OriginalString ?? BuildDeepLink();
                var shareMessage = string.Format(ShareDeepLinkMessage, Current.Name, deepLink);
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                var nativeShare = new NativeShare();
                nativeShare.SetText(shareMessage).Share();
#else
                UniClipboard.SetText(shareMessage);
                Messenger.Show(shareMessage);
#endif
            }
            else
                ExportGame();
        }

        private static string BuildDeepLink()
        {
            var deepLink = "https://cgs.link/?link=";
            deepLink += "https://www.cardgamesimulator.com/link?url%3D" +
                        HttpUtility.UrlEncode(HttpUtility.UrlEncode(Current.AutoUpdateUrl?.OriginalString));
            deepLink += "&apn=com.finoldigital.cardgamesim&isi=1392877362&ibi=com.finoldigital.CardGameSim";
            var regex = new Regex("[^a-zA-Z0-9 -]");
            var encodedName = regex.Replace(Current.Name, "+");
            deepLink += "&st=Card+Game+Simulator+-+" + encodedName + "&sd=Play+" + encodedName + "+on+CGS!";
            if (Current.BannerImageUrl != null && Current.BannerImageUrl.IsWellFormedOriginalString())
                deepLink += "&si=" + Current.BannerImageUrl.OriginalString;
            return deepLink;
        }

        private static void ExportGame()
        {
            var container = Path.Combine(UnityCardGame.GamesExportPath, UnityFileMethods.GetSafeFileName(Current.Id));
            if (Directory.Exists(container))
                Directory.Delete(container, true);

            var subContainer = Path.Combine(container, UnityFileMethods.GetSafeFileName(Current.Id));
            UnityFileMethods.CopyDirectory(Current.GameDirectoryPath, subContainer);

            var zipFileName = UnityFileMethods.GetSafeFileName(Current.Id + ".zip");
            UnityFileMethods.CreateZip(container, UnityCardGame.GamesExportPath, zipFileName);
            Directory.Delete(container, true);

            var targetZipFilePath = Path.Combine(UnityCardGame.GamesExportPath, zipFileName);
            var exportGameZipUri = new Uri(targetZipFilePath);

#if ENABLE_WINMD_SUPPORT
            var ExportGameErrorMessage = "ERROR: Failed to Export! ";
            UnityEngine.WSA.Application.InvokeOnUIThread(async () => {
                try
                {
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(exportGameZipUri.LocalPath);
                    if (file != null)
                    {
                        // Launch the retrieved file
                        var success = await Windows.System.Launcher.LaunchFileAsync(file);
                        if (!success)
                        {
                            Debug.LogError(ExportGameErrorMessage + exportGameZipUri.LocalPath);
                            CardGameManager.Instance.Messenger.Show(ExportGameErrorMessage + exportGameZipUri.LocalPath);
                        }
                    }
                    else
                    {
                        Debug.LogError(ExportGameErrorMessage + exportGameZipUri.LocalPath);
                        CardGameManager.Instance.Messenger.Show(ExportGameErrorMessage + exportGameZipUri.LocalPath);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e.Message + e.StackTrace);
                    CardGameManager.Instance.Messenger.Show(ExportGameErrorMessage + exportGameZipUri.LocalPath);
                }
            }, false);
#elif UNITY_ANDROID && !UNITY_EDITOR
            Instance.StartCoroutine(Instance.OpenZip(exportGameZipUri, CardGameManager.Current.Id));
#elif UNITY_IOS && !UNITY_EDITOR
            new NativeShare().AddFile(exportGameZipUri.AbsoluteUri).Share();
#else
            Application.OpenURL(exportGameZipUri.AbsoluteUri);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        public IEnumerator OpenZip(Uri uri, string gameId)
        {
            var uwr = new UnityWebRequest( uri, UnityWebRequest.kHttpVerbGET );
            var path = Path.Combine( Application.temporaryCachePath, gameId + ".zip" );
            uwr.downloadHandler = new DownloadHandlerFile( path );
            yield return uwr.SendWebRequest();
            new NativeShare().AddFile(path, "application/zip").Share();
        }
#endif

        private void LateUpdate()
        {
            Inputs.WasFocusBack = Inputs.IsFocusBack;
            Inputs.WasFocusNext = Inputs.IsFocusNext;
            Inputs.WasDown = Inputs.IsDown;
            Inputs.WasUp = Inputs.IsUp;
            Inputs.WasLeft = Inputs.IsLeft;
            Inputs.WasRight = Inputs.IsRight;
            Inputs.WasPageVertical = Inputs.IsPageVertical;
            Inputs.WasPageDown = Inputs.IsPageDown;
            Inputs.WasPageUp = Inputs.IsPageUp;
            Inputs.WasPageHorizontal = Inputs.IsPageHorizontal;
            Inputs.WasPageLeft = Inputs.IsPageLeft;
            Inputs.WasPageRight = Inputs.IsPageRight;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnApplicationQuit()
        {
            IsQuitting = true;
        }
    }
}
