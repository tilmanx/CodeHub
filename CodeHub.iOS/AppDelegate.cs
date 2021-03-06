﻿// --------------------------------------------------------------------------------------------------------------------
// <summary>
//    Defines the AppDelegate type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using CodeFramework.iOS;
using System.Collections.Generic;
using System;    
using Cirrious.CrossCore;
using Cirrious.MvvmCross.Touch.Platform;
using Cirrious.MvvmCross.ViewModels;
using MonoTouch.Foundation;
using MonoTouch.UIKit;
using CodeFramework.Core.Utils;
using CodeHub.Core.Services;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;

namespace CodeHub.iOS
{
    /// <summary>
    /// The UIApplicationDelegate for the application. This class is responsible for launching the 
    /// User Interface of the application, as well as listening (and optionally responding) to 
    /// application events from iOS.
    /// </summary>
    [Register("AppDelegate")]
    public class AppDelegate : MvxApplicationDelegate
    {
        /// <summary>
        /// The window.
        /// </summary>
        private UIWindow window;
		public string DeviceToken;

		/// <summary>
		/// This is the main entry point of the application.
		/// </summary>
		/// <param name="args">The args.</param>
		public static void Main(string[] args)
		{
			// if you want to use a different Application Delegate class from "AppDelegate"
			// you can specify it here.
			UIApplication.Main(args, null, "AppDelegate");
		}

        /// <summary>
        /// Finished the launching.
        /// </summary>
        /// <param name="app">The app.</param>
        /// <param name="options">The options.</param>
        /// <returns>True or false.</returns>
        public override bool FinishedLaunching(UIApplication app, NSDictionary options)
        {
			var iRate = MTiRate.iRate.SharedInstance;
			iRate.AppStoreID = 707173885;

			this.window = new UIWindow(UIScreen.MainScreen.Bounds);

            // Setup theme
            Theme.Setup();

            var presenter = new TouchViewPresenter(this.window);

            var setup = new Setup(this, presenter);
            setup.Initialize();

			Mvx.Resolve<CodeFramework.Core.Services.IAnalyticsService>().Init("UA-44040302-1", "CodeHub");

            if (options != null)
            {
                if (options.ContainsKey(UIApplication.LaunchOptionsRemoteNotificationKey)) 
                {
                    var remoteNotification = options[UIApplication.LaunchOptionsRemoteNotificationKey] as NSDictionary;
                    if(remoteNotification != null) {
                        HandleNotification(remoteNotification, true);
                    }
                }
            }

            var startup = Mvx.Resolve<IMvxAppStart>();
			startup.Start();

            this.window.MakeKeyAndVisible();

            InAppPurchases.Instance.PurchaseError += HandlePurchaseError;
            InAppPurchases.Instance.PurchaseSuccess += HandlePurchaseSuccess;

            var features = Mvx.Resolve<IFeaturesService>();

			// Notifications don't work on teh simulator so don't bother
            if (MonoTouch.ObjCRuntime.Runtime.Arch != MonoTouch.ObjCRuntime.Arch.SIMULATOR && features.IsPushNotificationsActivated)
			{
				const UIRemoteNotificationType notificationTypes = UIRemoteNotificationType.Alert | UIRemoteNotificationType.Badge;
				UIApplication.SharedApplication.RegisterForRemoteNotificationTypes(notificationTypes);
			}

            return true;
        }

        void HandlePurchaseSuccess (object sender, string e)
        {
            Mvx.Resolve<CodeFramework.Core.Services.IDefaultValueService>().Set(e, true);

            if (string.Equals(e, InAppPurchases.PushNotificationsId))
            {
                const UIRemoteNotificationType notificationTypes = UIRemoteNotificationType.Alert | UIRemoteNotificationType.Badge;
                UIApplication.SharedApplication.RegisterForRemoteNotificationTypes(notificationTypes);
            }
        }

        void HandlePurchaseError (object sender, Exception e)
        {
            MonoTouch.Utilities.ShowAlert("Unable to make purchase", e.Message);
        }

		public override void DidReceiveRemoteNotification(UIApplication application, NSDictionary userInfo, System.Action<UIBackgroundFetchResult> completionHandler)
		{
			if (application.ApplicationState == UIApplicationState.Active)
				return;
            HandleNotification(userInfo, false);
		}

        private void HandleNotification(NSDictionary data, bool fromBootup)
		{
			try
			{
				var viewDispatcher = Mvx.Resolve<Cirrious.MvvmCross.Views.IMvxViewDispatcher>();
                var appService = Mvx.Resolve<IApplicationService>();
                var repoId = new RepositoryIdentifier(data["r"].ToString());
                var parameters = new Dictionary<string, string>() {{"Username", repoId.Owner}, {"Repository", repoId.Name}};

                MvxViewModelRequest request;
                if (data.ContainsKey(new NSString("c")))
                {
                    request = MvxViewModelRequest<CodeHub.Core.ViewModels.ChangesetViewModel>.GetDefaultRequest();
                    parameters.Add("Node", data["c"].ToString());
                    parameters.Add("ShowRepository", "True");
                }
                else if (data.ContainsKey(new NSString("i")))
                {
                    request = MvxViewModelRequest<CodeHub.Core.ViewModels.Issues.IssueViewModel>.GetDefaultRequest();
                    parameters.Add("Id", data["i"].ToString());
                }
                else if (data.ContainsKey(new NSString("p")))
                {
                    request = MvxViewModelRequest<CodeHub.Core.ViewModels.PullRequests.PullRequestViewModel>.GetDefaultRequest();
                    parameters.Add("Id", data["p"].ToString());
                }
                else
                {
                    request = MvxViewModelRequest<CodeHub.Core.ViewModels.Repositories.RepositoryViewModel>.GetDefaultRequest();
                }

                request.ParameterValues = parameters;

                var username = data["u"].ToString();

                if (appService.Account == null || !appService.Account.Username.Equals(username))
                {
                    var user = appService.Accounts.FirstOrDefault(x => x.Username.Equals(username));
                    if (user != null)
                    {
                        appService.DeactivateUser();
                        appService.Accounts.SetDefault(user);
                    }
                }

                appService.SetUserActivationAction(() => viewDispatcher.ShowViewModel(request));

                if (appService.Account == null && !fromBootup)
                {
                    var startupViewModelRequest = MvxViewModelRequest<CodeHub.Core.ViewModels.App.StartupViewModel>.GetDefaultRequest();
                    viewDispatcher.ShowViewModel(startupViewModelRequest);
                }
			}
			catch (Exception e)
			{
				Console.WriteLine("Handle Notifications issue: " + e);
			}
		}

		public override void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
		{
			DeviceToken = deviceToken.Description.Trim('<', '>').Replace(" ", "");

            var app = Mvx.Resolve<IApplicationService>();
            if (app.Account != null && !app.Account.IsPushNotificationsEnabled.HasValue)
            {
                Task.Run(() => Mvx.Resolve<IPushNotificationsService>().Register());
                app.Account.IsPushNotificationsEnabled = true;
                app.Accounts.Update(app.Account);
            }
		}

		public override void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
		{
			MonoTouch.Utilities.ShowAlert("Error Registering for Notifications", error.LocalizedDescription);
		}

        public override bool OpenUrl(UIApplication application, NSUrl url, string sourceApplication, NSObject annotation)
        {
            try
            {
                var viewDispatcher = Mvx.Resolve<Cirrious.MvvmCross.Views.IMvxViewDispatcher>();
                var appService = Mvx.Resolve<IApplicationService>();

                var path = url.AbsoluteString.Replace("codehub://", "");
                var queryMarker = path.IndexOf("?", StringComparison.Ordinal);
                if (queryMarker > 0)
                    path = path.Substring(0, queryMarker);

                if (!path.EndsWith("/", StringComparison.Ordinal))
                    path += "/";
                var first = path.Substring(0, path.IndexOf("/", StringComparison.Ordinal));
                var firstIsDomain = first.Contains(".");

                var req = RouteProvider.ProcessRoute(path);
                if (req != null)
                    appService.SetUserActivationAction(() => viewDispatcher.ShowViewModel(req));
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to open URL \"" + url.AbsoluteString + "\": " + e.Message);
            }

            return false;
        }
    }

    public static class RouteProvider
    {
        public static Route[] Routes = {
            new Route("^[^/]*/stars/$", typeof(CodeHub.Core.ViewModels.Repositories.RepositoriesStarredViewModel)),
            new Route("^[^/]*/(?<Username>[^/]*)/$", typeof(CodeHub.Core.ViewModels.User.ProfileViewModel)),
            new Route("^[^/]*/(?<Username>[^/]*)/(?<Repository>[^/]*)/$", typeof(CodeHub.Core.ViewModels.Repositories.RepositoryViewModel)),
            new Route("^[^/]*/(?<Username>[^/]*)/(?<Repository>[^/]*)/pulls/$", typeof(CodeHub.Core.ViewModels.PullRequests.PullRequestsViewModel)),
            new Route("^[^/]*/(?<Username>[^/]*)/(?<Repository>[^/]*)/pull/(?<Id>[^/]*)/$", typeof(CodeHub.Core.ViewModels.PullRequests.PullRequestViewModel)),
            new Route("^[^/]*/(?<Username>[^/]*)/(?<Repository>[^/]*)/issues/$", typeof(CodeHub.Core.ViewModels.Issues.IssuesViewModel)),
            new Route("^[^/]*/(?<Username>[^/]*)/(?<Repository>[^/]*)/issues/(?<Id>[^/]*)/$", typeof(CodeHub.Core.ViewModels.Issues.IssueViewModel)),
            new Route("^[^/]*/(?<Username>[^/]*)/(?<Repository>[^/]*)/tree/(?<Branch>[^/]*)/(?<Path>.*)$", typeof(CodeHub.Core.ViewModels.Source.SourceTreeViewModel)),
        };

        public static MvxViewModelRequest ProcessRoute(string path)
        {
            if (!path.EndsWith("/", StringComparison.Ordinal))
                path += "/";

            foreach (var route in Routes)
            {
                var regex = new Regex(route.Path, RegexOptions.ExplicitCapture);
                var match = regex.Match(path);
                var groups = regex.GetGroupNames().Skip(1);

                if (match.Success)
                {
                    var rec = new MvxViewModelRequest();
                    rec.ViewModelType = route.ViewModelType;
                    rec.ParameterValues = new Dictionary<string, string>();
                    foreach (var group in groups)
                        rec.ParameterValues.Add(group, match.Groups[group].Value);
                    return rec;
                }
            }

            return null;
        }

    }

    public class Route
    {
        public string Path { get; set; }
        public Type ViewModelType { get; set; }

        public Route(string path, Type viewModelType) 
        {
            Path = path;
            ViewModelType = viewModelType;
        }
    }
}