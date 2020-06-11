﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Core;
using Umbraco.Core.Configuration;
using Umbraco.Core.Hosting;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Web.Common.Attributes;

namespace Umbraco.Web.BackOffice.Controllers
{
    [PluginController(Constants.Web.Mvc.BackOfficeApiArea)]
    public class BackOfficeAssetsController : UmbracoAuthorizedJsonController
    {
        private readonly IFileSystem _jsLibFileSystem;

        public BackOfficeAssetsController(IIOHelper ioHelper, IHostingEnvironment hostingEnvironment, ILogger logger, IGlobalSettings globalSettings)
        {
            _jsLibFileSystem = new PhysicalFileSystem(ioHelper, hostingEnvironment, logger, globalSettings.UmbracoPath + Path.DirectorySeparatorChar + "lib");
        }

        [HttpGet]
        public object GetSupportedLocales()
        {
            const string momentLocaleFolder = "moment";
            const string flatpickrLocaleFolder = "flatpickr/l10n";

            return new
            {
                moment = GetLocales(momentLocaleFolder),
                flatpickr = GetLocales(flatpickrLocaleFolder)
            };
        }

        private IEnumerable<string> GetLocales(string path)
        {
            var cultures = _jsLibFileSystem.GetFiles(path, "*.js").ToList();
            for (var i = 0; i < cultures.Count; i++)
            {
                cultures[i] = cultures[i]
                    .Substring(cultures[i].IndexOf(path, StringComparison.Ordinal) + path.Length + 1);
            }
            return cultures;
        }
    }
}
