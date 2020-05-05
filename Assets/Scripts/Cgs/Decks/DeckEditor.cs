﻿/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Linq;
using CardGameDef;
using CardGameDef.Unity;
using CardGameView;
using Cgs.Cards;
using Cgs.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cgs.Decks
{
    [RequireComponent(typeof(Canvas))]
    public class DeckEditor : MonoBehaviour, ICardDropHandler
    {
        public const string NewDeckPrompt = "Clear the editor and start a new Untitled deck?";
        public const string SaveChangesPrompt = "You have unsaved changes. Would you like to save?";
        public const string ChangeIndicator = "*";

        public float PreHeight => cardModelPrefab.GetComponent<RectTransform>().rect.height;

        public int CardsPerStack =>
            Mathf.FloorToInt((CardGameManager.PixelsPerInch * CardGameManager.Current.CardSize.Y / PreHeight) * 4);

        public int CardStackCount => Mathf.CeilToInt((float) CardGameManager.Current.DeckMaxCount / CardsPerStack);
        public List<CardStack> CardStacks => _cardStacks ?? (_cardStacks = new List<CardStack>());
        private List<CardStack> _cardStacks;

        public int CurrentCardStackIndex
        {
            get
            {
                if (_currentCardStackIndex < 0 || _currentCardStackIndex >= CardStacks.Count)
                    _currentCardStackIndex = 0;
                return _currentCardStackIndex;
            }
            set => _currentCardStackIndex = value;
        }

        private int _currentCardStackIndex;

        public DeckLoadMenu DeckLoader =>
            _deckLoader ?? (_deckLoader = Instantiate(deckLoadMenuPrefab).GetOrAddComponent<DeckLoadMenu>());

        private DeckLoadMenu _deckLoader;

        public DeckSaveMenu DeckSaver =>
            _deckSaver ?? (_deckSaver = Instantiate(deckSaveMenuPrefab).GetOrAddComponent<DeckSaveMenu>());

        private DeckSaveMenu _deckSaver;

        public List<CardModel> CardModels
        {
            get
            {
                List<CardModel> cardModels = new List<CardModel>();
                foreach (CardStack stack in CardStacks)
                    cardModels.AddRange(stack.GetComponentsInChildren<CardModel>());
                return cardModels;
            }
        }

        public UnityDeck CurrentDeck
        {
            get
            {
                var deck = new UnityDeck(CardGameManager.Current, SavedDeck?.Name ?? Deck.DefaultName,
                    CardGameManager.Current.DeckFileType);
                foreach (CardModel card in CardStacks.SelectMany(stack => stack.GetComponentsInChildren<CardModel>()))
                    deck.Add(card.Value);
                return deck;
            }
        }

        public Deck SavedDeck { get; private set; }

        public bool HasChanged
        {
            get
            {
                Deck currentDeck = CurrentDeck;
                if (currentDeck.Cards.Count < 1)
                    return false;
                return !currentDeck.Equals(SavedDeck);
            }
        }

        public GameObject cardViewerPrefab;
        public GameObject cardModelPrefab;
        public GameObject cardStackPrefab;
        public GameObject deckLoadMenuPrefab;
        public GameObject deckSaveMenuPrefab;
        public RectTransform layoutContent;
        public CardDropArea dropZone;
        public ScrollRect scrollRect;
        public Text nameText;
        public Text countText;
        public SearchResults searchResults;

        void OnEnable()
        {
            Instantiate(cardViewerPrefab);
            searchResults.HorizontalDoubleClickAction = AddCardModel;
            CardGameManager.Instance.OnSceneActions.Add(ResetCardStacks);
        }

        void Start()
        {
            CardGameManager.Instance.CardCanvases.Add(GetComponent<Canvas>());
            dropZone.dropHandler = this;
            ShowDeckLoadMenu();
        }

        void Update()
        {
            if (CardViewer.Instance.IsVisible || CardViewer.Instance.Zoom ||
                CardGameManager.Instance.ModalCanvas != null || searchResults.inputField.isFocused)
                return;

            if (Input.GetButtonDown(Inputs.Sort))
                Sort();
            else if (Input.GetButtonDown(Inputs.New))
                PromptForClear();
            else if (Input.GetButtonDown(Inputs.Load))
                ShowDeckLoadMenu();
            else if (Input.GetButtonDown(Inputs.Save))
                ShowDeckSaveMenu();
            else if (Input.GetButtonDown(Inputs.FocusBack) || Math.Abs(Input.GetAxis(Inputs.FocusBack)) >
                                                           Inputs.Tolerance
                                                           || Input.GetButtonDown(Inputs.FocusNext) ||
                                                           Math.Abs(Input.GetAxis(Inputs.FocusNext)) > Inputs.Tolerance)
                searchResults.inputField.ActivateInputField();
            else if (Input.GetButtonDown(Inputs.Filter))
                searchResults.ShowSearchMenu();
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetButtonDown(Inputs.Cancel))
                CheckBackToMainMenu();
        }

        public void ResetCardStacks()
        {
            Clear();
            layoutContent.DestroyAllChildren();
            CardStacks.Clear();
            for (var i = 0; i < CardStackCount; i++)
            {
                var cardStack = Instantiate(cardStackPrefab, layoutContent).GetOrAddComponent<CardStack>();
                cardStack.type = CardStackType.Vertical;
                cardStack.scrollRectContainer = scrollRect;
                cardStack.DoesImmediatelyRelease = true;
                cardStack.OnAddCardActions.Add(OnAddCardModel);
                cardStack.OnRemoveCardActions.Add(OnRemoveCardModel);
                CardStacks.Add(cardStack);
                cardStack.GetComponent<VerticalLayoutGroup>().spacing =
                    cardStackPrefab.GetComponent<VerticalLayoutGroup>().spacing
                    * (CardGameManager.PixelsPerInch * CardGameManager.Current.CardSize.Y / PreHeight);
            }

            layoutContent.sizeDelta = new Vector2(
                cardStackPrefab.GetComponent<RectTransform>().rect.width * CardStacks.Count,
                layoutContent.sizeDelta.y);
        }

        public void OnDrop(CardModel cardModel)
        {
            AddCardModel(cardModel);
        }

        public void AddCardModel(CardModel cardModel)
        {
            if (cardModel == null || CardStacks.Count < 1)
                return;

            EventSystem.current.SetSelectedGameObject(null, cardModel.CurrentPointerEventData);

            AddCard(cardModel.Value);
        }

        public void AddCard(UnityCard card)
        {
            if (card == null || CardStacks.Count < 1)
                return;

            int maxCopiesInStack = CardsPerStack;
            CardModel newCardModel = null;
            while (newCardModel == null)
            {
                if (CardStacks[CurrentCardStackIndex].transform.childCount < maxCopiesInStack)
                {
                    newCardModel = Instantiate(cardModelPrefab, CardStacks[CurrentCardStackIndex].transform)
                        .GetOrAddComponent<CardModel>();
                    newCardModel.Value = card;
                }
                else
                {
                    CurrentCardStackIndex++;
                    if (CurrentCardStackIndex == 0)
                        maxCopiesInStack++;
                }
            }

            OnAddCardModel(CardStacks[CurrentCardStackIndex], newCardModel);
        }

        public void OnAddCardModel(CardStack cardStack, CardModel cardModel)
        {
            if (cardStack == null || cardModel == null)
                return;

            CurrentCardStackIndex = CardStacks.IndexOf(cardStack);
            cardModel.SecondaryDragAction = cardModel.UpdateParentCardStackScrollRect;
            cardModel.DoubleClickAction = DestroyCardModel;

            FocusScrollRectOn(cardModel);

            UpdateDeckStats();
        }

        public void FocusScrollRectOn(CardModel cardModel)
        {
            if (cardModel == null || cardModel.ParentCardStack == null)
                return;

            int cardStackIndex = CardStacks.IndexOf(cardModel.ParentCardStack);
            if (cardStackIndex > 0 && cardStackIndex < CardStacks.Count)
                scrollRect.horizontalNormalizedPosition = CardStacks.Count > 1
                    ? cardStackIndex / (CardStacks.Count - 1f)
                    : 0f;
        }

        public void OnRemoveCardModel(CardStack cardStack, CardModel cardModel)
        {
            UpdateDeckStats();
        }

        public void DestroyCardModel(CardModel cardModel)
        {
            if (cardModel == null)
                return;

            cardModel.transform.SetParent(null);
            Destroy(cardModel.gameObject);
            CardViewer.Instance.IsVisible = false;

            UpdateDeckStats();
        }

        public void Sort()
        {
            UnityDeck sortedDeck = CurrentDeck;
            sortedDeck.Sort();
            foreach (CardStack stack in CardStacks)
                stack.transform.DestroyAllChildren();
            CurrentCardStackIndex = 0;
            foreach (Card card in sortedDeck.Cards)
                AddCard((UnityCard) card);
        }

        public void PromptForClear()
        {
            CardGameManager.Instance.Messenger.Prompt(NewDeckPrompt, Clear);
        }

        public void Clear()
        {
            foreach (CardStack stack in CardStacks)
                stack.transform.DestroyAllChildren();
            CurrentCardStackIndex = 0;
            scrollRect.horizontalNormalizedPosition = 0;

            CardViewer.Instance.IsVisible = false;
            SavedDeck = null;
            UpdateDeckStats();
        }

        public string UpdateDeckName(string newName)
        {
            if (newName == null)
                newName = string.Empty;
            newName = UnityExtensionMethods.GetSafeFileName(newName);
            nameText.text = newName + (HasChanged ? ChangeIndicator : string.Empty);
            return newName;
        }

        public void UpdateDeckStats()
        {
            string deckName = Deck.DefaultName;
            if (SavedDeck != null)
                deckName = SavedDeck.Name;
            nameText.text = deckName + (HasChanged ? ChangeIndicator : string.Empty);
            countText.text = CurrentDeck.Cards.Count.ToString();
        }

        public void ShowDeckLoadMenu()
        {
            Deck currentDeck = CurrentDeck;
            DeckLoader.Show(LoadDeck, currentDeck.Name, currentDeck.Cards.Count > 0 ? currentDeck.ToString() : null);
        }

        public void LoadDeck(UnityDeck deck)
        {
            if (deck == null)
                return;

            Clear();
            foreach (Card card in deck.Cards)
                AddCard((UnityCard) card);
            SavedDeck = deck;
            UpdateDeckStats();
        }

        public void ShowDeckSaveMenu()
        {
            UnityDeck deckToSave = CurrentDeck;
            bool overwrite = SavedDeck != null && deckToSave.Name.Equals(SavedDeck.Name);
            DeckSaver.Show(deckToSave, UpdateDeckName, OnSaveDeck, overwrite);
        }

        public void OnSaveDeck(Deck savedDeck)
        {
            SavedDeck = savedDeck;
            UpdateDeckStats();
        }

        public void CheckBackToMainMenu()
        {
            if (HasChanged)
            {
                CardGameManager.Instance.Messenger.Ask(SaveChangesPrompt, BackToMainMenu, ShowDeckSaveMenu);
                return;
            }

            BackToMainMenu();
        }

        public void BackToMainMenu()
        {
            SceneManager.LoadScene(MainMenu.MainMenuSceneIndex);
        }
    }
}