// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using AIDevGallery.Samples.Attributes;
using AIDevGallery.Samples.SharedCode;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace AIDevGallery.Samples.OpenSourceModels
{
    [GallerySample(
        Model1Types = [ModelType.MobileNet],
        Scenario = ScenarioType.ImageClassifyImage,
        NugetPackageReferences = [
            "System.Drawing.Common",
            "Microsoft.ML.OnnxRuntime.DirectML",
            "Microsoft.ML.OnnxRuntime.Extensions"
        ],
        SharedCode = [
            SharedCodeEnum.Prediction,
            SharedCodeEnum.ImageNetLabels,
            SharedCodeEnum.ImageNet,
            SharedCodeEnum.BitmapFunctions,
            SharedCodeEnum.DeviceUtils
        ],
        Name = "MobileNet Image Classification",
        Id = "09d73ba7-b877-45f9-9de6-41898ab4d339",
        Icon = "\uE8B9")]
    internal sealed partial class ImageClassificationMobileNet : Page
    {
        private InferenceSession? _inferenceSession;

        public ImageClassificationMobileNet()
        {
            this.Unloaded += (s, e) => _inferenceSession?.Dispose();
            this.Loaded += (s, e) => Page_Loaded(); // <exclude-line>
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is SampleNavigationParameters sampleParams)
            {
                await InitModel(sampleParams.ModelPath);
                sampleParams.NotifyCompletion();

                await ClassifyImage(Path.Join(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "team.jpg"));
            }
        }

        // <exclude>
        private void Page_Loaded()
        {
            UploadImageButton.Focus(FocusState.Programmatic);
        }

        // </exclude>
        private Task InitModel(string modelPath)
        {
            return Task.Run(() =>
            {
                if (_inferenceSession != null)
                {
                    return;
                }

                SessionOptions sessionOptions = new();
                sessionOptions.RegisterOrtExtensions();

                _inferenceSession = new InferenceSession(modelPath, sessionOptions);
            });
        }

        private async void UploadImageButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new Window();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            var picker = new FileOpenPicker();

            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".jpg");

            picker.ViewMode = PickerViewMode.Thumbnail;

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                UploadImageButton.Focus(FocusState.Programmatic);
                await ClassifyImage(file.Path);
            }
        }

        private async Task ClassifyImage(string filePath)
        {
            if (!Path.Exists(filePath))
            {
                return;
            }

            BitmapImage bitmapImage = new(new Uri(filePath));
            UploadedImage.Source = bitmapImage;
            NarratorHelper.AnnounceImageChanged(UploadedImage, "Image changed: new upload."); // <exclude-line>

            var predictions = await Task.Run(() =>
            {
                Bitmap image = new(filePath);

                // Resize image
                int width = 224;
                int height = 224;
                var resizedImage = BitmapFunctions.ResizeBitmap(image, width, height);
                image.Dispose();
                image = resizedImage;

                // Preprocess image
                Tensor<float> input = new DenseTensor<float>([1, 3, 224, 224]);
                input = BitmapFunctions.PreprocessBitmapWithStdDev(image, input);
                image.Dispose();

                // Setup inputs
                var inputMetadataName = _inferenceSession!.InputNames[0];
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(inputMetadataName, input)
                };

                // Run inference
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _inferenceSession!.Run(inputs);

                // Postprocess to get softmax vector
                IEnumerable<float> output = results[0].AsEnumerable<float>();
                return ImageNet.GetSoftmax(output);
            });

            // Populates table of results
            ImageNet.DisplayPredictions(predictions, PredictionsStackPanel);
        }
    }
}