﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using Nop.Admin.Extensions;
using Nop.Admin.Models.Common;
using Nop.Core;
using Nop.Core.Caching;


using Nop.Core.Domain.Seo;
using Nop.Core.Plugins;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;

using Nop.Services.Security;
using Nop.Services.Seo;

using Nop.Services.Stores;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Kendoui;
using Nop.Web.Framework.Security;

namespace Nop.Admin.Controllers
{
    public partial class CommonController : BaseAdminController
    {
        #region Fields
        private readonly ICustomerService _customerService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IWebHelper _webHelper;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ILanguageService _languageService;
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IPermissionService _permissionService;
        private readonly ILocalizationService _localizationService;
        private readonly ISearchTermService _searchTermService;
        private readonly IStoreService _storeService;
        private readonly HttpContextBase _httpContext;

        #endregion

        #region Constructors

        public CommonController( 
            ICustomerService customerService, 
            IUrlRecordService urlRecordService, 
            IWebHelper webHelper,
            IDateTimeHelper dateTimeHelper,
            ILanguageService languageService, 
            IWorkContext workContext,
            IStoreContext storeContext,
            IPermissionService permissionService, 
            ILocalizationService localizationService,
            ISearchTermService searchTermService,
            IStoreService storeService,
            HttpContextBase httpContext)
        {
            this._customerService = customerService;
            this._urlRecordService = urlRecordService;
            this._webHelper = webHelper;
          
            this._dateTimeHelper = dateTimeHelper;
            this._languageService = languageService;
            this._workContext = workContext;
            this._storeContext = storeContext;
            this._permissionService = permissionService;
            this._localizationService = localizationService;
            this._searchTermService = searchTermService;
            this._storeService = storeService;
            this._httpContext = httpContext;
        }

        #endregion

        #region Methods

        public ActionResult SystemInfo()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var model = new SystemInfoModel();
            model.NopVersion = NopVersion.CurrentVersion;
            try
            {
                model.OperatingSystem = Environment.OSVersion.VersionString;
            }
            catch (Exception) { }
            try
            {
                model.AspNetInfo = RuntimeEnvironment.GetSystemVersion();
            }
            catch (Exception) { }
            try
            {
                model.IsFullTrust = AppDomain.CurrentDomain.IsFullyTrusted.ToString();
            }
            catch (Exception) { }
            model.ServerTimeZone = TimeZone.CurrentTimeZone.StandardName;
            model.ServerLocalTime = DateTime.Now;
            model.UtcTime = DateTime.UtcNow;
            model.HttpHost = _webHelper.ServerVariables("HTTP_HOST");
            foreach (var key in _httpContext.Request.ServerVariables.AllKeys)
            {
                model.ServerVariables.Add(new SystemInfoModel.ServerVariableModel
                {
                    Name = key,
                    Value = _httpContext.Request.ServerVariables[key]
                });
            }
            //Environment.GetEnvironmentVariable("USERNAME");
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                model.LoadedAssemblies.Add(new SystemInfoModel.LoadedAssembly
                {
                    FullName =  assembly.FullName,
                    //we cannot use Location property in medium trust
                    //Location = assembly.Location
                });
            }
            return View(model);
        }

        public ActionResult Warnings()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var model = new List<SystemWarningModel>();

            //store URL
            var currentStoreUrl = _storeContext.CurrentStore.Url;
            if (!String.IsNullOrEmpty(currentStoreUrl) &&
                (currentStoreUrl.Equals(_webHelper.GetStoreLocation(false), StringComparison.InvariantCultureIgnoreCase)
                ||
                currentStoreUrl.Equals(_webHelper.GetStoreLocation(true), StringComparison.InvariantCultureIgnoreCase)
                ))
                model.Add(new SystemWarningModel
                    {
                        Level = SystemWarningLevel.Pass,
                        Text = _localizationService.GetResource("Admin.System.Warnings.URL.Match")
                    });
            else
                model.Add(new SystemWarningModel
                {
                    Level = SystemWarningLevel.Warning,
                    Text = string.Format(_localizationService.GetResource("Admin.System.Warnings.URL.NoMatch"), currentStoreUrl, _webHelper.GetStoreLocation(false))
                });
          

            //incompatible plugins
            if (PluginManager.IncompatiblePlugins != null)
                foreach (var pluginName in PluginManager.IncompatiblePlugins)
                    model.Add(new SystemWarningModel
                    {
                        Level = SystemWarningLevel.Warning,
                        Text = string.Format(_localizationService.GetResource("Admin.System.Warnings.IncompatiblePlugin"), pluginName )
                    });
            

            //validate write permissions (the same procedure like during installation)
            var dirPermissionsOk = true;
            var dirsToCheck = FilePermissionHelper.GetDirectoriesWrite(_webHelper);
            foreach (string dir in dirsToCheck)
                if (!FilePermissionHelper.CheckPermissions(dir, false, true, true, false))
                {
                    model.Add(new SystemWarningModel
                    {
                        Level = SystemWarningLevel.Warning,
                        Text = string.Format(_localizationService.GetResource("Admin.System.Warnings.DirectoryPermission.Wrong"), WindowsIdentity.GetCurrent().Name, dir)
                    });
                    dirPermissionsOk = false;
                }
            if (dirPermissionsOk)
                model.Add(new SystemWarningModel
                {
                    Level = SystemWarningLevel.Pass,
                    Text = _localizationService.GetResource("Admin.System.Warnings.DirectoryPermission.OK")
                });

            var filePermissionsOk = true;
            var filesToCheck = FilePermissionHelper.GetFilesWrite(_webHelper);
            foreach (string file in filesToCheck)
                if (!FilePermissionHelper.CheckPermissions(file, false, true, true, true))
                {
                    model.Add(new SystemWarningModel
                    {
                        Level = SystemWarningLevel.Warning,
                        Text = string.Format(_localizationService.GetResource("Admin.System.Warnings.FilePermission.Wrong"), WindowsIdentity.GetCurrent().Name, file)
                    });
                    filePermissionsOk = false;
                }
            if (filePermissionsOk)
                model.Add(new SystemWarningModel
                {
                    Level = SystemWarningLevel.Pass,
                    Text = _localizationService.GetResource("Admin.System.Warnings.FilePermission.OK")
                });

            //machine key
            try
            {
                var machineKeySection = ConfigurationManager.GetSection("system.web/machineKey") as MachineKeySection;
                var machineKeySpecified = machineKeySection != null &&
                    !String.IsNullOrEmpty(machineKeySection.DecryptionKey) &&
                    !machineKeySection.DecryptionKey.StartsWith("AutoGenerate",StringComparison.InvariantCultureIgnoreCase);

                if (!machineKeySpecified)
                {
                    model.Add(new SystemWarningModel
                    {
                        Level = SystemWarningLevel.Warning,
                        Text = _localizationService.GetResource("Admin.System.Warnings.MachineKey.NotSpecified")
                    });
                }
                else
                {
                    model.Add(new SystemWarningModel
                    {
                        Level = SystemWarningLevel.Pass,
                        Text = _localizationService.GetResource("Admin.System.Warnings.MachineKey.Specified")
                    });
                }
            }
            catch (Exception exc)
            {
                LogException(exc);
            }
            
            return View(model);
        }

        public ActionResult Maintenance()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var model = new MaintenanceModel();
            model.DeleteGuests.EndDate = DateTime.UtcNow.AddDays(-7);
            model.DeleteGuests.OnlyWithoutShoppingCart = true;
            model.DeleteAbandonedCarts.OlderThan = DateTime.UtcNow.AddDays(-182);
            return View(model);
        }

        [HttpPost, ActionName("Maintenance")]
        [FormValueRequired("delete-guests")]
        public ActionResult MaintenanceDeleteGuests(MaintenanceModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            DateTime? startDateValue = (model.DeleteGuests.StartDate == null) ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.DeleteGuests.StartDate.Value, _dateTimeHelper.CurrentTimeZone);

            DateTime? endDateValue = (model.DeleteGuests.EndDate == null) ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.DeleteGuests.EndDate.Value, _dateTimeHelper.CurrentTimeZone).AddDays(1);

            model.DeleteGuests.NumberOfDeletedCustomers = _customerService.DeleteGuestCustomers(startDateValue, endDateValue, model.DeleteGuests.OnlyWithoutShoppingCart);

            return View(model);
        }
      

        [HttpPost, ActionName("Maintenance")]
        [FormValueRequired("delete-exported-files")]
        public ActionResult MaintenanceDeleteFiles(MaintenanceModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            DateTime? startDateValue = (model.DeleteExportedFiles.StartDate == null) ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.DeleteExportedFiles.StartDate.Value, _dateTimeHelper.CurrentTimeZone);

            DateTime? endDateValue = (model.DeleteExportedFiles.EndDate == null) ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.DeleteExportedFiles.EndDate.Value, _dateTimeHelper.CurrentTimeZone).AddDays(1);


            model.DeleteExportedFiles.NumberOfDeletedFiles = 0;
            string path = Path.Combine(this.Request.PhysicalApplicationPath, "content\\files\\exportimport");
            foreach (var fullPath in Directory.GetFiles(path))
            {
                try
                {
                    var fileName = Path.GetFileName(fullPath);
                    if (fileName.Equals("index.htm", StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    var info = new FileInfo(fullPath);
                    if ((!startDateValue.HasValue || startDateValue.Value < info.CreationTimeUtc)&&
                        (!endDateValue.HasValue || info.CreationTimeUtc < endDateValue.Value))
                    {
                        System.IO.File.Delete(fullPath);
                        model.DeleteExportedFiles.NumberOfDeletedFiles++;
                    }
                }
                catch (Exception exc)
                {
                    ErrorNotification(exc, false);
                }
            }

            return View(model);
        }



        //language
        [ChildActionOnly]
        public ActionResult LanguageSelector()
        {
            var model = new LanguageSelectorModel();
            model.CurrentLanguage = _workContext.WorkingLanguage.ToModel();
            model.AvailableLanguages = _languageService
                .GetAllLanguages(storeId: _storeContext.CurrentStore.Id)
                .Select(x => x.ToModel())
                .ToList();
            return PartialView(model);
        }
        public ActionResult SetLanguage(int langid, string returnUrl = "")
        {
            var language = _languageService.GetLanguageById(langid);
            if (language != null)
            {
                _workContext.WorkingLanguage = language;
            }

            //home page
            if (String.IsNullOrEmpty(returnUrl))
                returnUrl = Url.Action("Index", "Home", new { area = "Admin" });
            return Redirect(returnUrl);
        }

        public ActionResult ClearCache()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var cacheManager = new MemoryCacheManager();
            cacheManager.Clear();

            return RedirectToAction("Index", "Home");
        }

        public ActionResult RestartApplication()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            //restart application
            _webHelper.RestartAppDomain();
            return RedirectToAction("Index", "Home");
        }

        #region Searh engine friendly names

        public ActionResult SeNames()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var model = new UrlRecordListModel();
            return View(model);
        }

        [HttpPost]
        public ActionResult SeNames(DataSourceRequest command, UrlRecordListModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var urlRecords = _urlRecordService.GetAllUrlRecords(model.SeName, command.Page - 1, command.PageSize);
            var gridModel = new DataSourceResult
            {
                Data = urlRecords.Select(x =>
                {
                    string languageName;
                    if (x.LanguageId == 0)
                    {
                        languageName = _localizationService.GetResource("Admin.System.SeNames.Language.Standard");
                    }
                    else
                    {
                        var language = _languageService.GetLanguageById(x.LanguageId);
                        languageName = language != null ? language.Name : "Unknown";
                    }
                    return new UrlRecordModel
                    {
                        Id = x.Id,
                        Name = x.Slug,
                        EntityId = x.EntityId,
                        EntityName = x.EntityName,
                        IsActive = x.IsActive,
                        Language = languageName,
                    };
                }),
                Total = urlRecords.TotalCount
            };
            return Json(gridModel);
        }

        [HttpPost]
        public ActionResult DeleteSelectedSeNames(ICollection<int> selectedIds)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            if (selectedIds != null)
            {
                var urlRecords = new List<UrlRecord>();
                foreach (var id in selectedIds)
                {
                    var urlRecord = _urlRecordService.GetUrlRecordById(id);
                    if (urlRecord != null)
                        urlRecords.Add(urlRecord);
                }
                foreach (var urlRecord in urlRecords)
                    _urlRecordService.DeleteUrlRecord(urlRecord);
            }

            return Json(new { Result = true });
        }

        #endregion

        [ChildActionOnly]
        public ActionResult PopularSearchTermsReport()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageProducts))
                return Content("");

            return PartialView();
        }

        [HttpPost]
        public ActionResult PopularSearchTermsReport(DataSourceRequest command)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageProducts))
                return AccessDeniedView();

            var searchTermRecordLines = _searchTermService.GetStats(command.Page - 1, command.PageSize);
            var gridModel = new DataSourceResult
            {
                Data = searchTermRecordLines.Select(x => new SearchTermReportLineModel
                {
                    Keyword = x.Keyword,
                    Count = x.Count,
                }),
                Total = searchTermRecordLines.TotalCount
            };
            return Json(gridModel);
        }

        #endregion
    }
}
