﻿using System;
using System.Collections.Generic;
using System.Data.Entity.Design.PluralizationServices;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using Coevery;
using Coevery.Common.Extensions;
using Coevery.Common.Models;
using Coevery.ContentManagement;
using Coevery.ContentManagement.Aspects;
using Coevery.ContentManagement.MetaData;
using Coevery.ContentManagement.MetaData.Models;
using Coevery.Core.Common.Models;
using Coevery.Core.Containers.Models;
using Coevery.Core.Contents;
using Coevery.Mvc;
using Coevery.Core.Contents.Settings;
using Coevery.Localization;
using Coevery.Logging;
using Coevery.Mvc.Extensions;
using Coevery.Mvc.Html;
using Coevery.UI.Notify;
using Coevery.Utility.Extensions;
using Coevery.UI.Navigation;

namespace Coevery.Common.Controllers {
    public class ViewTemplateController : Controller, IUpdateModel {
        private readonly IContentDefinitionManager _contentDefinitionManager;
        private readonly INavigationManager _navigationManager;
        private readonly IContentDefinitionExtension _contentDefinitionExtension;

        public ViewTemplateController(
            ICoeveryServices coeveryServices,
            IContentDefinitionManager contentDefinitionManager,
            IContentDefinitionExtension contentDefinitionExtension,
            INavigationManager navigationManager) {
            _contentDefinitionExtension = contentDefinitionExtension;
            Services = coeveryServices;
            _contentDefinitionManager = contentDefinitionManager;

            T = NullLocalizer.Instance;
            Logger = NullLogger.Instance;

            _navigationManager = navigationManager;
        }

        public ICoeveryServices Services { get; private set; }
        public Localizer T { get; set; }
        public ILogger Logger { get; set; }

        public ActionResult MenuList() {
            const string menuName = "FrontMenu";
            IEnumerable<MenuItem> menuItems = _navigationManager.BuildMenu(menuName);
            return View(menuItems);
        }

        public ActionResult List(string id) {
            if (string.IsNullOrEmpty(id)) {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            var contentItem = Services.ContentManager.New("ListViewPage");
            var model = Services.ContentManager.BuildDisplay(contentItem);

            return View(model);
        }

        public ActionResult Create(string id, int? containerId) {
            if (string.IsNullOrEmpty(id)) {
                return CreatableTypeList(containerId);
            }
            var contentItem = Services.ContentManager.New(_contentDefinitionExtension.GetEntityNameFromCollectionName(id));

            if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Cannot create content"))) {
                return new HttpUnauthorizedResult();
            }

            if (containerId.HasValue && contentItem.Is<ContainablePart>()) {
                var common = contentItem.As<CommonPart>();
                if (common != null) {
                    common.Container = Services.ContentManager.Get(containerId.Value);
                }
            }

            dynamic itemShape = Services.ContentManager.BuildEditor(contentItem);

            var page = Services.ContentManager.New<CreatePagePart>("CreatePage");
            page.Item = contentItem;
            var model = Services.ContentManager.BuildEditor(page);
            model.Content.Add(itemShape);

            return View(model);
        }

        private ActionResult CreatableTypeList(int? containerId) {
            dynamic viewModel = Services.New.ViewModel(ContentTypes: GetCreatableTypes(containerId.HasValue), ContainerId: containerId);

            // Casting to avoid invalid (under medium trust) reflection over the protected View method and force a static invocation.
            return View("CreatableTypeList", (object) viewModel);
        }

        private IEnumerable<ContentTypeDefinition> GetCreatableTypes(bool andContainable) {
            return _contentDefinitionManager.ListTypeDefinitions().Where(ctd => ctd.Settings.GetModel<ContentTypeSettings>().Creatable && (!andContainable || ctd.Parts.Any(p => p.PartDefinition.Name == "ContainablePart")));
        }

        [HttpPost, ActionName("Create")]
        [FormValueRequired("submit.Save")]
        public ActionResult CreatePOST(string id, string returnUrl) {
            return CreatePOST(id, returnUrl, contentItem => {
                if (!contentItem.Has<IPublishingControlAspect>() && !contentItem.TypeDefinition.Settings.GetModel<ContentTypeSettings>().Draftable) {
                    Services.ContentManager.Publish(contentItem);
                }
            });
        }

        private ActionResult CreatePOST(string id, string returnUrl, Action<ContentItem> conditionallyPublish) {
            var contentItem = Services.ContentManager.New(id);

            if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Couldn't create content"))) {
                return new HttpUnauthorizedResult();
            }

            Services.ContentManager.Create(contentItem, VersionOptions.Draft);

            Services.ContentManager.UpdateEditor(contentItem, this);
            if (!ModelState.IsValid) {
                ModelState.AddModelError("CreateError", T("The creation didn't change model state.").ToString());
                Services.TransactionManager.Cancel();
                Response.StatusCode = (int) HttpStatusCode.BadRequest;
                var temp = (from values in ModelState
                    from error in values.Value.Errors
                    select error.ErrorMessage).ToArray();
                return Content(string.Concat(temp));
            }

            conditionallyPublish(contentItem);

            Services.Notifier.Information(string.IsNullOrWhiteSpace(contentItem.TypeDefinition.DisplayName)
                ? T("Your content has been created.")
                : T("Your {0} has been created.", contentItem.TypeDefinition.DisplayName));
            if (!string.IsNullOrEmpty(returnUrl)) {
                return this.RedirectLocal(returnUrl);
            }

            return Json(new {Id = contentItem.Id});
        }

        public ActionResult Edit(int id) {
            var contentItem = Services.ContentManager.Get(id, VersionOptions.Latest);

            if (contentItem == null) {
                return HttpNotFound();
            }

            if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Cannot edit content"))) {
                return new HttpUnauthorizedResult();
            }

            dynamic itemShape = Services.ContentManager.BuildEditor(contentItem);

            var page = Services.ContentManager.New<EditPagePart>("EditPage");
            page.Item = contentItem;
            var model = Services.ContentManager.BuildEditor(page);
            model.Content.Add(itemShape);

            // Casting to avoid invalid (under medium trust) reflection over the protected View method and force a static invocation.
            return View((object) model);
        }

        [HttpPost, ActionName("Edit")]
        [FormValueRequired("submit.Save")]
        public ActionResult EditPOST(int id, string returnUrl) {
            return EditPOST(id, returnUrl, contentItem => {
                if (!contentItem.Has<IPublishingControlAspect>() && !contentItem.TypeDefinition.Settings.GetModel<ContentTypeSettings>().Draftable) {
                    Services.ContentManager.Publish(contentItem);
                }
            });
        }

        public ActionResult View(int id) {
            var contentItem = Services.ContentManager.Get(id, VersionOptions.Latest);
            if (contentItem == null) {
                return HttpNotFound();
            }

            dynamic itemShape = Services.ContentManager.BuildDisplay(contentItem);

            var page = Services.ContentManager.New<ViewPagePart>("ViewPage");
            page.Item = contentItem;
            var model = Services.ContentManager.BuildDisplay(page);
            model.Content.Add(itemShape);
            return View(model);
        }

        [HttpPost]
        public ActionResult Remove(int id, string returnUrl) {
            var contentItem = Services.ContentManager.Get(id, VersionOptions.Latest);

            if (!Services.Authorizer.Authorize(Permissions.DeleteContent, contentItem, T("Couldn't remove content"))) {
                return new HttpUnauthorizedResult();
            }

            if (contentItem != null) {
                Services.ContentManager.Remove(contentItem);
                Services.Notifier.Information(string.IsNullOrWhiteSpace(contentItem.TypeDefinition.DisplayName)
                    ? T("That content has been removed.")
                    : T("That {0} has been removed.", contentItem.TypeDefinition.DisplayName));
            }

            return this.RedirectLocal(returnUrl, () => RedirectToAction("List"));
        }

        private ActionResult EditPOST(int id, string returnUrl, Action<ContentItem> conditionallyPublish) {
            var contentItem = Services.ContentManager.Get(id, VersionOptions.DraftRequired);

            if (contentItem == null) {
                return HttpNotFound();
            }

            if (!Services.Authorizer.Authorize(Permissions.EditContent, contentItem, T("Couldn't edit content"))) {
                return new HttpUnauthorizedResult();
            }

            string previousRoute = null;
            if (contentItem.Has<IAliasAspect>()
                && !string.IsNullOrWhiteSpace(returnUrl)
                && Request.IsLocalUrl(returnUrl)
                // only if the original returnUrl is the content itself
                && String.Equals(returnUrl, Url.ItemDisplayUrl(contentItem), StringComparison.OrdinalIgnoreCase)) {
                previousRoute = contentItem.As<IAliasAspect>().Path;
            }

            dynamic model = Services.ContentManager.UpdateEditor(contentItem, this);
            if (!ModelState.IsValid) {
                Services.TransactionManager.Cancel();
                // Casting to avoid invalid (under medium trust) reflection over the protected View method and force a static invocation.
                Response.StatusCode = (int) HttpStatusCode.BadRequest;
                var temp = (from values in ModelState
                    from error in values.Value.Errors
                    select error.ErrorMessage).ToArray();
                return Content(string.Concat(temp));
            }

            conditionallyPublish(contentItem);

            if (!string.IsNullOrWhiteSpace(returnUrl)
                && previousRoute != null
                && !String.Equals(contentItem.As<IAliasAspect>().Path, previousRoute, StringComparison.OrdinalIgnoreCase)) {
                returnUrl = Url.ItemDisplayUrl(contentItem);
            }

            Services.Notifier.Information(string.IsNullOrWhiteSpace(contentItem.TypeDefinition.DisplayName)
                ? T("Your content has been saved.")
                : T("Your {0} has been saved.", contentItem.TypeDefinition.DisplayName));

            // return this.RedirectLocal(returnUrl, () => RedirectToAction("Edit", new RouteValueDictionary { { "Id", contentItem.Id } }));
            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }

        bool IUpdateModel.TryUpdateModel<TModel>(TModel model, string prefix, string[] includeProperties, string[] excludeProperties) {
            return TryUpdateModel(model, prefix, includeProperties, excludeProperties);
        }

        void IUpdateModel.AddModelError(string key, LocalizedString errorMessage) {
            ModelState.AddModelError(key, errorMessage.ToString());
        }
    }
}