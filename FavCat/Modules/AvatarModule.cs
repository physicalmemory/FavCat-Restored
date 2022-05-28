using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FavCat.Adapters;
using FavCat.CustomLists;
using FavCat.Database.Stored;
using MelonLoader;
using UIExpansionKit.API;
using UnhollowerRuntimeLib;
using UnityEngine;
using UnityEngine.UI;
using VRC.Core;
using VRC.UI;

namespace FavCat.Modules
{
    public sealed class AvatarModule : ExtendedFavoritesModuleBase<StoredAvatar>
    {
        private readonly PageAvatar myPageAvatar;
        
        private string myCurrentUiAvatarId = "";

        private readonly bool myInitialised;

        public AvatarModule() : base(ExpandedMenu.AvatarMenu, FavCatMod.Database.AvatarFavorites, GetListsParent())
        {
            MelonDebug.Msg("Adding button to UI - Looking up for Change Button");
            var foundAvatarPage = Resources.FindObjectsOfTypeAll<PageAvatar>()?.FirstOrDefault(p => p.transform.Find("Change Button") != null);
            if (foundAvatarPage == null)
                throw new ApplicationException("No avatar page, can't initialize extended favorites");
            
            myPageAvatar = foundAvatarPage;
            
            ExpansionKitApi.GetExpandedMenu(ExpandedMenu.UserDetailsMenu).AddSimpleButton("Search known public avatars", DoSearchKnownAvatars);

            var expandEnforcer = new GameObject(ExpandEnforcerGameObjectName, new[] {Il2CppType.Of<RectTransform>(), Il2CppType.Of<LayoutElement>()});
            expandEnforcer.transform.SetParent(GetListsParent(), false);
            var layoutElement = expandEnforcer.GetComponent<LayoutElement>();
            layoutElement.minWidth = 1534;
            layoutElement.minHeight = 0;

            myInitialised = true;
        }

        private void DoSearchKnownAvatars()
        {
            if (FavCatMod.PageUserInfo == null)
                return;
            
            FavCatMod.GetUiManager().Method_Public_Void_String_Boolean_0("UserInterface/MenuContent/Screens/Avatar", false);
            SetSearchListHeaderAndScrollToIt("Search running...");
            LastSearchRequest = "Created by " + FavCatMod.PageUserInfo.field_Private_APIUser_0.displayName;
            FavCatMod.Database.RunBackgroundAvatarSearchByUser(FavCatMod.PageUserInfo.field_Private_APIUser_0.id, AcceptSearchResult);
        }

        private static Transform GetListsParent()
        {
            var foundAvatarPage = GameObject.Find("UserInterface/MenuContent/Screens/Avatar");
            if (foundAvatarPage == null)
                throw new ApplicationException("No avatar page, can't initialize extended favorites");

            var randomList = foundAvatarPage.GetComponentInChildren<UiAvatarList>(true);
            return randomList.transform.parent;
            
        }
        
        protected override void OnFavButtonClicked(StoredCategory storedCategory)
        {
            ApiAvatar currentApiAvatar = myPageAvatar.field_Public_SimpleAvatarPedestal_0.field_Internal_ApiAvatar_0;
            OnFavButtonClicked(storedCategory, currentApiAvatar.id, false);
        }

        private void OnFavButtonClicked(StoredCategory storedCategory, string avatarId, bool disallowRecursiveRequests)
        {
            if (FavCatMod.Database.myStoredAvatars.FindById(avatarId) == null)
            {
                if (disallowRecursiveRequests)
                    return;
                
                // something showed an unknown avatar, request it before favoriting
                new ApiAvatar { id = avatarId }.Fetch(new Action<ApiContainer>(model =>
                {
                    FavCatMod.Database?.UpdateStoredAvatar(model.Model.Cast<ApiAvatar>());
                    MelonCoroutines.Start(ReFavAfterDelay(storedCategory, avatarId));
                }));
                return;
            }

            if (FavCatMod.Database.AvatarFavorites.IsFavorite(avatarId, storedCategory.CategoryName))
                FavCatMod.Database.AvatarFavorites.DeleteFavorite(avatarId, storedCategory.CategoryName);
            else
                FavCatMod.Database.AvatarFavorites.AddFavorite(avatarId, storedCategory.CategoryName);
        }

        private IEnumerator ReFavAfterDelay(StoredCategory category, string id)
        {
            yield return new WaitForSeconds(0.25f);
            OnFavButtonClicked(category, id, true);
        }

        protected internal override void RefreshFavButtons()
        {
            var apiAvatar = myPageAvatar != null ? myPageAvatar.field_Public_SimpleAvatarPedestal_0 != null ? myPageAvatar.field_Public_SimpleAvatarPedestal_0.field_Internal_ApiAvatar_0 : null : null;

            foreach (var customPickerList in PickerLists)
            {
                bool favorited = FavCatMod.Database.AvatarFavorites.IsFavorite(myCurrentUiAvatarId, customPickerList.Key);
                    
                var isNonPublic = apiAvatar?.releaseStatus != "public";
                if (favorited)
                    customPickerList.Value.SetFavButtonText(isNonPublic ? "Unfav (p)" : "Unfav", true);
                else
                    customPickerList.Value.SetFavButtonText(isNonPublic ? "Fav (p)": "Fav", true);
            }
        }

        protected override void OnPickerSelected(IPickerElement model)
        {
            PlaySound();

            if (FavCatSettings.AvatarSearchMode.Value == "author")
            {
                FavCatMod.Instance.PlayerModule?.OnPickerSelected(((IStoredModelAdapter<StoredAvatar>)model).Model.AuthorId, listsParent.gameObject); //TO-FIX
                return;
            }

            var avatar = new ApiAvatar() {id = model.Id};
            if (MelonDebug.IsEnabled())
                MelonDebug.Msg($"Performing an API request for {model.Id}");
            avatar.Fetch(new Action<ApiContainer>((_) =>
            {
                if (MelonDebug.IsEnabled())
                    MelonDebug.Msg($"Done an API request for {model.Id}");

                FavCatMod.Database?.UpdateStoredAvatar(avatar);

                myPageAvatar.field_Public_SimpleAvatarPedestal_0.Refresh(avatar);

                // VRC has a tendency to change visibility of its lists after pedestal refresh 
                ReorderLists();
                RefreshFavButtons();
            }), new Action<ApiContainer>(c =>
            {
                if (MelonDebug.IsEnabled())
                    MelonDebug.Msg("API request errored with " + c.Code + " - " + c.Error);
                if (c.Code == 404 && listsParent.gameObject.activeInHierarchy)
                {
                    FavCatMod.Database.CompletelyDeleteAvatar(model.Id);
                    var menu = ExpansionKitApi.CreateCustomFullMenuPopup(LayoutDescription.WideSlimList);
                    menu.AddSpacer();
                    menu.AddSpacer();
                    menu.AddLabel("This avatar is not available anymore (deleted or privated)");
                    menu.AddLabel("It has been removed from all favorite lists");
                    menu.AddSpacer();
                    menu.AddSpacer();
                    menu.AddSpacer();
                    menu.AddSimpleButton("Close", menu.Hide);
                    menu.Show();
                }
            }));
        }

        internal override void Update()
        {
            if (!myInitialised) return;
            
            base.Update();

            var pedestal = myPageAvatar.field_Public_SimpleAvatarPedestal_0;
            if (pedestal == null) return;
            var apiAvatar = pedestal.field_Internal_ApiAvatar_0;
            if (apiAvatar == null) return;
            if (apiAvatar.id == myCurrentUiAvatarId) return;

            myCurrentUiAvatarId = apiAvatar.id ?? "";

            RefreshFavButtons();
            if (apiAvatar.Populated)
                FavCatMod.Database?.UpdateStoredAvatar(apiAvatar);
        }

        protected override void SearchButtonClicked()
        {
            BuiltinUiUtils.ShowInputPopup("Local Search (Avatar)", "", InputField.InputType.Standard, false,
                "Search!", (s, list, arg3) =>
                {
                    SetSearchListHeaderAndScrollToIt("Search running...");
                    LastSearchRequest = s;
                    FavCatMod.Database.RunBackgroundAvatarSearch(s, AcceptSearchResult);
                });
        }

        protected override bool FavButtonsOnLists => true; //REMOVE?
        protected override IPickerElement WrapModel(StoredFavorite? favorite, StoredAvatar model) => new DbAvatarAdapter(model, favorite);

        protected override void SortModelList(string sortCriteria, string category, List<(StoredFavorite?, StoredAvatar)> avatars)
        {
            var inverted = sortCriteria.Length > 0 && sortCriteria[0] == '!';
            Comparison<(StoredFavorite? Fav, StoredAvatar Model)> comparison;
            switch (sortCriteria)
            {
                case "name":
                case "!name":
                default:
                    comparison = (a, b) => string.Compare(a.Model.Name, b.Model.Name, StringComparison.InvariantCultureIgnoreCase) * (inverted ? -1 : 1); 
                    break;
                case "updated":
                case "!updated":
                    comparison = (a, b) => a.Model.UpdatedAt.CompareTo(b.Model.UpdatedAt) * (inverted ? -1 : 1);
                    break;
                case "created":
                case "!created":
                    comparison = (a, b) => a.Model.CreatedAt.CompareTo(b.Model.CreatedAt) * (inverted ? -1 : 1);
                    break;
                case "added":
                case "!added":
                    comparison = (a, b) => (a.Fav?.AddedOn ?? DateTime.MinValue).CompareTo(b.Fav?.AddedOn ?? DateTime.MinValue) * (inverted ? -1 : 1);
                    break;
            }
            avatars.Sort(comparison);
        }
    }
}