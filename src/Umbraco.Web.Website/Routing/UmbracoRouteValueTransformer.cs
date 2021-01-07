using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Configuration.Models;
using Umbraco.Core.Hosting;
using Umbraco.Core.Strings;
using Umbraco.Extensions;
using Umbraco.Web.Common.Controllers;
using Umbraco.Web.Common.Routing;
using Umbraco.Web.Routing;
using Umbraco.Web.Website.Controllers;

namespace Umbraco.Web.Website.Routing
{
    /// <summary>
    /// The route value transformer for Umbraco front-end routes
    /// </summary>
    /// <remarks>
    /// NOTE: In aspnet 5 DynamicRouteValueTransformer has been improved, see https://github.com/dotnet/aspnetcore/issues/21471
    /// It seems as though with the "State" parameter we could more easily assign the IPublishedRequest or IPublishedContent
    /// or UmbracoContext more easily that way. In the meantime we will rely on assigning the IPublishedRequest to the
    /// route values along with the IPublishedContent to the umbraco context
    /// have created a GH discussion here https://github.com/dotnet/aspnetcore/discussions/28562 we'll see if anyone responds
    /// </remarks>
    public class UmbracoRouteValueTransformer : DynamicRouteValueTransformer
    {
        private readonly ILogger<UmbracoRouteValueTransformer> _logger;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly IUmbracoRenderingDefaults _renderingDefaults;
        private readonly IShortStringHelper _shortStringHelper;
        private readonly IActionDescriptorCollectionProvider _actionDescriptorCollectionProvider;
        private readonly IPublishedRouter _publishedRouter;
        private readonly GlobalSettings _globalSettings;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IRuntimeState _runtime;

        /// <summary>
        /// Initializes a new instance of the <see cref="UmbracoRouteValueTransformer"/> class.
        /// </summary>
        public UmbracoRouteValueTransformer(
            ILogger<UmbracoRouteValueTransformer> logger,
            IUmbracoContextAccessor umbracoContextAccessor,
            IUmbracoRenderingDefaults renderingDefaults,
            IShortStringHelper shortStringHelper,
            IActionDescriptorCollectionProvider actionDescriptorCollectionProvider,
            IPublishedRouter publishedRouter,
            IOptions<GlobalSettings> globalSettings,
            IHostingEnvironment hostingEnvironment,
            IRuntimeState runtime)
        {
            _logger = logger;
            _umbracoContextAccessor = umbracoContextAccessor;
            _renderingDefaults = renderingDefaults;
            _shortStringHelper = shortStringHelper;
            _actionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
            _publishedRouter = publishedRouter;
            _globalSettings = globalSettings.Value;
            _hostingEnvironment = hostingEnvironment;
            _runtime = runtime;
        }

        /// <inheritdoc/>
        public override async ValueTask<RouteValueDictionary> TransformAsync(HttpContext httpContext, RouteValueDictionary values)
        {
            // If we aren't running, then we have nothing to route
            if (_runtime.Level != RuntimeLevel.Run)
            {
                return values;
            }

            // will be null for any client side requests like JS, etc...
            if (_umbracoContextAccessor.UmbracoContext == null)
            {
                return values;
            }

            // Check for back office request
            // TODO: This is how the module was doing it before but could just as easily be part of the RoutableDocumentFilter
            // which still needs to be migrated.
            if (httpContext.Request.IsDefaultBackOfficeRequest(_globalSettings, _hostingEnvironment))
            {
                return values;
            }

            // Check if there is no existing content and return the no content controller
            if (!_umbracoContextAccessor.UmbracoContext.Content.HasContent())
            {
                values["controller"] = ControllerExtensions.GetControllerName<RenderNoContentController>();
                values["action"] = nameof(RenderNoContentController.Index);

                return await Task.FromResult(values);
            }

            IPublishedRequest publishedRequest = await RouteRequestAsync(_umbracoContextAccessor.UmbracoContext);

            UmbracoRouteValues routeDef = GetUmbracoRouteDefinition(httpContext, values, publishedRequest);

            values["controller"] = routeDef.ControllerName;
            if (string.IsNullOrWhiteSpace(routeDef.ActionName) == false)
            {
                values["action"] = routeDef.ActionName;
            }

            return await Task.FromResult(values);
        }

        /// <summary>
        /// Returns a <see cref="UmbracoRouteValues"/> object based on the current content request
        /// </summary>
        private UmbracoRouteValues GetUmbracoRouteDefinition(HttpContext httpContext, RouteValueDictionary values, IPublishedRequest request)
        {
            if (httpContext is null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            Type defaultControllerType = _renderingDefaults.DefaultControllerType;
            var defaultControllerName = ControllerExtensions.GetControllerName(defaultControllerType);

            string customActionName = null;

            // check that a template is defined), if it doesn't and there is a hijacked route it will just route
            // to the index Action
            if (request.HasTemplate())
            {
                // the template Alias should always be already saved with a safe name.
                // if there are hyphens in the name and there is a hijacked route, then the Action will need to be attributed
                // with the action name attribute.
                customActionName = request.GetTemplateAlias()?.Split('.')[0].ToSafeAlias(_shortStringHelper);
            }

            // creates the default route definition which maps to the 'UmbracoController' controller
            var def = new UmbracoRouteValues(
                request,
                defaultControllerName,
                defaultControllerType,
                templateName: customActionName);

            var customControllerName = request.PublishedContent?.ContentType.Alias;
            if (customControllerName != null)
            {
                def = DetermineHijackedRoute(def, customControllerName, customActionName, request);
            }

            // store the route definition
            values.TryAdd(Constants.Web.UmbracoRouteDefinitionDataToken, def);

            return def;
        }

        private UmbracoRouteValues DetermineHijackedRoute(UmbracoRouteValues routeValues, string customControllerName, string customActionName, IPublishedRequest request)
        {
            IReadOnlyList<ControllerActionDescriptor> candidates = FindControllerCandidates(customControllerName, customActionName, routeValues.ActionName);

            // check if there's a custom controller assigned, base on the document type alias.
            var customControllerCandidates = candidates.Where(x => x.ControllerName.InvariantEquals(customControllerName)).ToList();

            // check if that custom controller exists
            if (customControllerCandidates.Count > 0)
            {
                ControllerActionDescriptor controllerDescriptor = customControllerCandidates[0];

                // ensure the controller is of type IRenderController and ControllerBase
                if (TypeHelper.IsTypeAssignableFrom<IRenderController>(controllerDescriptor.ControllerTypeInfo)
                    && TypeHelper.IsTypeAssignableFrom<ControllerBase>(controllerDescriptor.ControllerTypeInfo))
                {
                    // now check if the custom action matches
                    var customActionExists = customActionName != null && customControllerCandidates.Any(x => x.ActionName.InvariantEquals(customActionName));

                    // it's a hijacked route with a custom controller, so return the the values
                    return new UmbracoRouteValues(
                        request,
                        controllerDescriptor.ControllerName,
                        controllerDescriptor.ControllerTypeInfo,
                        customActionExists ? customActionName : routeValues.ActionName,
                        customActionName,
                        true); // Hijacked = true
                }
                else
                {
                    _logger.LogWarning(
                        "The current Document Type {ContentTypeAlias} matches a locally declared controller of type {ControllerName}. Custom Controllers for Umbraco routing must implement '{UmbracoRenderController}' and inherit from '{UmbracoControllerBase}'.",
                        request.PublishedContent.ContentType.Alias,
                        controllerDescriptor.ControllerTypeInfo.FullName,
                        typeof(IRenderController).FullName,
                        typeof(ControllerBase).FullName);

                    // we cannot route to this custom controller since it is not of the correct type so we'll continue with the defaults
                    // that have already been set above.
                }
            }

            return routeValues;
        }

        /// <summary>
        /// Return a list of controller candidates that match the custom controller and action names
        /// </summary>
        private IReadOnlyList<ControllerActionDescriptor> FindControllerCandidates(string customControllerName, string customActionName, string defaultActionName)
        {
            var descriptors = _actionDescriptorCollectionProvider.ActionDescriptors.Items
                .Cast<ControllerActionDescriptor>()
                .Where(x => x.ControllerName.InvariantEquals(customControllerName)
                        && (x.ActionName.InvariantEquals(defaultActionName) || (customActionName != null && x.ActionName.InvariantEquals(customActionName))))
                .ToList();

            return descriptors;
        }

        private async Task<IPublishedRequest> RouteRequestAsync(IUmbracoContext umbracoContext)
        {
            // ok, process

            // instantiate, prepare and process the published content request
            // important to use CleanedUmbracoUrl - lowercase path-only version of the current url
            IPublishedRequestBuilder requestBuilder = await _publishedRouter.CreateRequestAsync(umbracoContext.CleanedUmbracoUrl);

            // TODO: This is ugly with the re-assignment to umbraco context but at least its now
            // an immutable object. The only way to make this better would be to have a RouteRequest
            // as part of UmbracoContext but then it will require a PublishedRouter dependency so not sure that's worth it.
            // Maybe could be a one-time Set method instead?
            return umbracoContext.PublishedRequest = await _publishedRouter.RouteRequestAsync(requestBuilder);

            // // HandleHttpResponseStatus returns a value indicating that the request should
            // // not be processed any further, eg because it has been redirect. then, exit.
            // if (UmbracoModule.HandleHttpResponseStatus(httpContext, request, _logger))
            //    return;
            // if (!request.HasPublishedContent == false)
            // {
            //     // httpContext.RemapHandler(new PublishedContentNotFoundHandler());
            // }
            // else
            // {
            //     // RewriteToUmbracoHandler(httpContext, request);
            // }
        }
    }
}
