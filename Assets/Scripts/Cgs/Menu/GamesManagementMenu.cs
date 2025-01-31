/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Linq;
using Cgs.UI;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityExtensionMethods;
#if !UNITY_ANDROID && !UNITY_IOS
using SimpleFileBrowser;
#endif

namespace Cgs.Menu
{
    [RequireComponent(typeof(Modal))]
    public class GamesManagementMenu : SelectionPanel
    {
        public const string ImportGamePrompt = "Download from Web URL,\n or Load from ZIP File?";
        public const string DownloadFromWeb = "Download from Web URL";
        public const string LoadFromFile = "Load from ZIP File";
        public const string SelectZipFilePrompt = "Select ZIP File";

        public const string DownloadLabel = "Download Game";
        public const string DownloadPrompt = "Enter CGS AutoUpdate URL...";

        private const string CgsGamesUrl = "https://cgs.games";

        public static string NoSyncMessage => $"{CardGameManager.Current.Name} does not have a CGS AutoUpdate URL!";

        public GameObject cardGameEditorMenuPrefab;
        public GameObject gameImportModalPrefab;
        public GameObject downloadMenuPrefab;

        protected override bool AllowSwitchOff => false;

        private Modal Menu => _menu ??= gameObject.GetOrAddComponent<Modal>();

        private Modal _menu;

        private CardGameEditorMenu CardGameEditor =>
            _cardGameEditor ??= Instantiate(cardGameEditorMenuPrefab).GetOrAddComponent<CardGameEditorMenu>();

        private CardGameEditorMenu _cardGameEditor;

        private DecisionModal ImportModal =>
            _importModal ??= Instantiate(gameImportModalPrefab).GetOrAddComponent<DecisionModal>();

        private DecisionModal _importModal;

        private DownloadMenu Downloader => _downloader ??= Instantiate(downloadMenuPrefab)
            .GetOrAddComponent<DownloadMenu>();

        private DownloadMenu _downloader;

#if UNITY_ANDROID || UNITY_IOS
        private static string _zipFileType;
#endif

        private void OnEnable()
        {
            CardGameManager.Instance.OnSceneActions.Add(BuildGameSelectionOptions);
        }

        private void Start()
        {
#if UNITY_ANDROID || UNITY_IOS
            _zipFileType = NativeFilePicker.ConvertExtensionToFileType("zip");
#endif
        }

        private void Update()
        {
            if (!Menu.IsFocused)
                return;

            if (Inputs.IsVertical)
            {
                if (Inputs.IsUp && !Inputs.WasUp)
                    SelectPrevious();
                else if (Inputs.IsDown && !Inputs.WasDown)
                    SelectNext();
            }

            if (Input.GetKeyDown(Inputs.BluetoothReturn) && Toggles.Select(toggle => toggle.gameObject)
                    .Contains(EventSystem.current.currentSelectedGameObject))
                EventSystem.current.currentSelectedGameObject.GetComponent<Toggle>().isOn = true;
            else if (Inputs.IsSubmit)
                Sync();
            else if (Inputs.IsNew)
                CreateNew();
            else if (Inputs.IsLoad)
                Import();
            else if (Inputs.IsSave)
                Share();
            else if (Inputs.IsOption)
                Delete();
            else if (Inputs.IsPageVertical && !Inputs.WasPageVertical)
                ScrollPage(Inputs.IsPageDown);
            else if (Inputs.IsCancel)
                Hide();
        }

        public void Show()
        {
            Menu.Show();
            BuildGameSelectionOptions();
        }

        private void BuildGameSelectionOptions()
        {
            Rebuild(CardGameManager.Instance.AllCardGames, SelectGame, CardGameManager.Current.Id);
        }

        [UsedImplicitly]
        public void SelectGame(Toggle toggle, string gameId)
        {
            if (toggle != null && toggle.isOn && gameId != CardGameManager.Current.Id)
                CardGameManager.Instance.Select(gameId);
        }

        [UsedImplicitly]
        public void CreateNew()
        {
            if (Settings.DeveloperMode)
                CardGameEditor.Show();
            else
                Application.OpenURL(CgsGamesUrl);
        }

        [UsedImplicitly]
        public void Import()
        {
            ImportModal.Show(ImportGamePrompt, new Tuple<string, UnityAction>(DownloadFromWeb, ShowDownloader),
                new Tuple<string, UnityAction>(LoadFromFile, ShowFileLoader));
        }

        private void ShowDownloader()
        {
            Downloader.Show(DownloadLabel, DownloadPrompt, CardGameManager.Instance.GetCardGame, true);
        }

        private static void ShowFileLoader()
        {
#if UNITY_ANDROID || UNITY_IOS
            var permission = NativeFilePicker.PickFile(path =>
            {
                if (path == null)
                    Debug.Log("Operation cancelled");
                else
                    CardGameManager.Instance.ImportCardGame(path);
            }, _zipFileType);
            Debug.Log( "Permission result: " + permission );
#else
            FileBrowser.ShowLoadDialog((paths) => CardGameManager.Instance.ImportCardGame(paths[0]),
                () => { }, FileBrowser.PickMode.Files, false, null, null,
                SelectZipFilePrompt);
#endif
        }

        [UsedImplicitly]
        public void Share()
        {
            CardGameManager.Instance.Share();
        }

        [UsedImplicitly]
        public void Delete()
        {
            CardGameManager.Instance.PromptDelete();
        }

        [UsedImplicitly]
        public void Sync()
        {
            if (CardGameManager.Current.AutoUpdateUrl?.IsWellFormedOriginalString() ?? false)
                CardGameManager.Instance.StartCoroutine(
                    CardGameManager.Instance.UpdateCardGame(CardGameManager.Current));
            else
                CardGameManager.Instance.Messenger.Show(NoSyncMessage);
            Hide();
        }

        [UsedImplicitly]
        public void Hide()
        {
            Menu.Hide();
        }
    }
}
