/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using UnityEngine;

namespace Cgs.Decks
{
    public class DeckEditorLayout : MonoBehaviour
    {
        private const float MinWidth = 1200;

        private static readonly Vector2 DeckButtonsPortraitAnchor = new Vector2(0, 0.43f);
        private static readonly Vector2 DeckButtonsLandscapePosition = new Vector2(-650, 0);

        private static readonly Vector2 SelectButtonsPortraitPosition = new Vector2(0, 10);
        private static readonly Vector2 SelectButtonsLandscapePosition = new Vector2(-350, 10);

        public RectTransform deckButtons;
        public RectTransform selectButtons;

        private void OnRectTransformDimensionsChange()
        {
            if (!gameObject.activeInHierarchy)
                return;

            if (((RectTransform) transform).rect.width < MinWidth) // Portrait
            {
                deckButtons.anchorMin = DeckButtonsPortraitAnchor;
                deckButtons.anchorMax = DeckButtonsPortraitAnchor;
                deckButtons.pivot = Vector2.up;
                deckButtons.anchoredPosition = Vector2.zero;
                selectButtons.anchoredPosition = SelectButtonsPortraitPosition;
            }
            else // Landscape
            {
                deckButtons.anchorMin = Vector2.one;
                deckButtons.anchorMax = Vector2.one;
                deckButtons.pivot = Vector2.one;
                deckButtons.anchoredPosition = DeckButtonsLandscapePosition;
                selectButtons.anchoredPosition = SelectButtonsLandscapePosition;
            }
        }
    }
}
