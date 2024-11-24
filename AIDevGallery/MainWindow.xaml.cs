// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using AIDevGallery.Pages;
using AIDevGallery.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.System;

namespace AIDevGallery
{
    internal sealed partial class MainWindow : Window //WinUIEx.WindowEx
    {
        public MainWindow(object? obj = null)
        {
            this.InitializeComponent();
//#if WINDOWS
//            this.Width = 1280;
//            this.MinWidth = 640;
//            this.MinHeight = 480;
//#endif

            SetTitleBar();
            App.ModelCache.DownloadQueue.ModelsChanged += DownloadQueue_ModelsChanged;

            this.NavView.Loaded += (sender, args) =>
            {
                NavigateToPage(obj);
            };
        }

        public void NavigateToPage(object? obj)
        {
            if (obj is Scenario)
            {
                Navigate("Samples", obj);
            }
            else if (obj is ModelType or List<ModelType>)
            {
                Navigate("Models", obj);
            }
            else
            {
                Navigate("Home");
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            Navigate(args.InvokedItem.ToString()!);
        }

        public void Navigate(string Tag, object? obj = null)
        {
            Tag = Tag.ToLower(CultureInfo.CurrentCulture);

            switch (Tag)
            {
                case "home":
                    Navigate(typeof(HomePage));
                    break;
                case "samples":
                    Navigate(typeof(ScenarioSelectionPage), obj);
                    break;
                case "models":
                    Navigate(typeof(ModelSelectionPage), obj);
                    break;
                case "guides":
                    Navigate(typeof(GuidesPage));
                    break;
                case "contribute":
                    _ = Launcher.LaunchUriAsync(new Uri("https://aka.ms/ai-dev-gallery"));
                    break;
                case "settings":
                    Navigate(typeof(SettingsPage), obj);
                    break;
            }
        }

        private void Navigate(Type page, object? param = null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                NavFrame.Navigate(page, param);
            });
        }

        public void Navigate(MostRecentlyUsedItem mru)
        {
            if (mru.Type == MostRecentlyUsedItemType.Model)
            {
                Navigate("models", mru);
            }
            else
            {
                Navigate("samples", mru);
            }
        }

        public void Navigate(Sample sample)
        {
            Navigate("samples", sample);
        }

        public void Navigate(SearchResult result)
        {
            if (result.Tag is Scenario scenario)
            {
                Navigate("samples", scenario);
            }
            else if (result.Tag is ModelType modelType)
            {
                Navigate("models", modelType);
            }
        }

        private void SetTitleBar()
        {
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(titleBar);
            titleBar.Window = this;
            this.AppWindow.SetIcon("Assets/AppIcon/Icon.ico");

            this.Title = Windows.ApplicationModel.Package.Current.DisplayName;

            if (this.Title.EndsWith("Dev", StringComparison.InvariantCulture))
            {
                titleBar.Subtitle = "Dev";
            }
            else if (this.Title.EndsWith("Preview", StringComparison.InvariantCulture))
            {
                titleBar.Subtitle = "Preview";
            }
        }

        private void DownloadQueue_ModelsChanged(ModelDownloadQueue sender)
        {
            DownloadProgressPanel.Visibility = Visibility.Visible;
            DownloadProgressRing.IsActive = sender.GetDownloads().Count > 0;
            DownloadFlyout.ShowAt(DownloadBtn);
        }

        private void ManageModelsClicked(object sender, RoutedEventArgs e)
        {
            NavFrame.Navigate(typeof(SettingsPage), "ModelManagement");
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                var filteredSearchResults = App.SearchIndex.Where(sr => sr.Label.Contains(sender.Text, StringComparison.OrdinalIgnoreCase)).ToList();
                SearchBox.ItemsSource = filteredSearchResults.OrderByDescending(i => i.Label.StartsWith(sender.Text, StringComparison.CurrentCultureIgnoreCase)).ThenBy(i => i.Label);
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is SearchResult result)
            {
                Navigate(result);
            }

            SearchBox.Text = string.Empty;
        }

        private void TitleBar_BackButtonClick(object sender, RoutedEventArgs e)
        {
            if (NavFrame.CanGoBack)
            {
                NavFrame.GoBack();
            }
        }

        private void NavFrame_Navigating(object sender, Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs e)
        {
            if (e.SourcePageType == typeof(ScenarioSelectionPage))
            {
                NavView.SelectedItem = NavView.MenuItems[1];
            }
            else if (e.SourcePageType == typeof(ModelSelectionPage))
            {
                NavView.SelectedItem = NavView.MenuItems[2];
            }
            else if (e.SourcePageType == typeof(GuidesPage))
            {
                NavView.SelectedItem = NavView.MenuItems[3];
            }
            else if (e.SourcePageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = NavView.FooterMenuItems[1];
            }
            else
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
        }
    }
}