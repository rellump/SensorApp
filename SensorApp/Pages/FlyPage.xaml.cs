﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Devices.Sensors;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using DatabaseClient;
using SensorApp.Annotations;
using SensorApp.Classes;

namespace SensorApp {
    public sealed partial class FlyPage : INotifyPropertyChanged {
        private readonly Accelerometer _accelerometer;
        private App _app;
        private Image[] _groundImages;
        private Image[] _skyImages;
        private Image[] _mountainImages;
        private MediaElement[] _mediaElements;

        public GameState State { get; set; }

        public FlyPage() {
            InitializeComponent();
            DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;
            State = new GameState();
            _app = (App) Application.Current;
            _accelerometer = Accelerometer.GetDefault();
            if (_accelerometer != null) {
                var minReportInterval = _accelerometer.MinimumReportInterval;
                var reportInterval = minReportInterval > 16 ? minReportInterval : 16;
                _accelerometer.ReportInterval = reportInterval;
                _accelerometer.ReadingTransform = DisplayOrientations.Landscape;
            }
            DebugInfo.Visibility = _app.GameSettings.ShowDebugInfo ? Visibility.Visible : Visibility.Collapsed;

            Loaded += async (sender, args) => {
                await InitWindow();
                if (State.IsRunning) {
                    StartFirstUpdate();
                }
                else {
                    UpdateWindow.Visibility = Visibility.Collapsed;
                    PauseWindow.Visibility = Visibility.Visible;
                    Blackscreen.Opacity = 0;
                }
                _accelerometer.ReadingChanged += ReadingChanged;
            };

            Unloaded += (sender, args) => { StopUpdate(); };
        }

        private async void ReadingChanged(object sender, AccelerometerReadingChangedEventArgs e) {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                var reading = e.Reading;
                State.Angles = Angles.CalculateAngles(reading);
                Airplane.RenderTransform = new RotateTransform {Angle = -State.Angles.X};

                if (State.IsRunning)
                    Update();

                OnPropertyChanged(nameof(State));
            });
        }

        #region UIMethods

        private void StartButton_Click(object sender, RoutedEventArgs e) { StartUpdate(); }

        private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            //MyHelpers.SaveGameState(State, "GameState");
            await WebConnection.CreateNewGameState(new ServerGameState(State));
        }

        private async void SettingsButton_OnClick(object sender, RoutedEventArgs e) {
            var previousSettings = new GameSettings(_app.GameSettings);

            var result = await SettingsContentDialog.ShowAsync();
            if (result == ContentDialogResult.Primary) {
                DebugInfo.Visibility = _app.GameSettings.ShowDebugInfo ? Visibility.Visible : Visibility.Collapsed;
                foreach (var mediaElement in _mediaElements) {
                    mediaElement.IsMuted = _app.GameSettings.SoundMuted;
                }
                MyHelpers.SaveGameSettings(_app.GameSettings);
            }
            else {
                _app.GameSettings = previousSettings;
            }
        }

        private void SettingsContentDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) {
            EnableDebugInfoCheckBox.IsChecked = _app.GameSettings.ShowDebugInfo;
            MuteSoundCheckBox.IsChecked = _app.GameSettings.SoundMuted;
        }

        private void EnableDebugInfoCheckBox_Checked(object sender, RoutedEventArgs e) { _app.GameSettings.ShowDebugInfo = true; }

        private void EnableDebugInfoCheckBox_Unchecked(object sender, RoutedEventArgs e) { _app.GameSettings.ShowDebugInfo = false; }

        private void MuteSoundCheckBox_Checked(object sender, RoutedEventArgs e) { _app.GameSettings.SoundMuted = true; }

        private void MuteSoundCheckBox_Unchecked(object sender, RoutedEventArgs e) { _app.GameSettings.SoundMuted = false; }

        protected override void OnNavigatedTo(NavigationEventArgs e) {
            base.OnNavigatedTo(e);
            var state = e.Parameter as GameState;
            if (state != null) {
                State = state;
            }
        }

        #endregion

        #region Update

        private void StartFirstUpdate() {
            State.ResetSpeeds().ResetAngles().GetNextLocation();
            UpdateWindow.Visibility = Visibility.Visible;
            PauseWindow.Visibility = Visibility.Collapsed;
            Blackscreen.Opacity = 0;
            State.IsRunning = true;
        }

        private void StartUpdate() {
            State.IsRunning = true;
            FadeInInitialBlackscreenStoryboard.Begin();
        }

        private void StopUpdate() {
            State.IsRunning = false;
            FadeInInitialBlackscreenStoryboard.Begin();
        }

        // Main Update Method
        private void Update() {
            // Calculate Speeds
            CalculateSpeedY();
            CalculateSpeedX();
            // Check if it should stop
            if (State.SpeedY < GameSettings.MinSpeedY) {
                StopUpdate();
                return;
            }
            // Updates
            UpdateGround();
            UpdateSky();
            UpdateMountain();
            UpdateScore();
        }

        // Update ground
        private void UpdateGround() {
            foreach (var groundImage in _groundImages) {
                var top = Canvas.GetTop(groundImage);
                var left = Canvas.GetLeft(groundImage);
                var newTop = top + State.SpeedY;
                var newLeft = left;

                if (State.Angles.X > 0) {
                    newLeft = left + State.SpeedX;
                }
                else if (State.Angles.X < 0) {
                    newLeft = left - State.SpeedX;
                }

                PositionInCanvas(groundImage, newLeft, newTop);
                CheckGroundOutOfBound(groundImage);
                State.Position = new Point(State.Position.X + left - newLeft, State.Position.Y + top - newTop);
            }
        }

        // Update sky
        private void UpdateSky() {
            foreach (var skyImage in _skyImages) {
                var top = Canvas.GetTop(skyImage);
                var left = Canvas.GetLeft(skyImage);
                var newTop = top - State.SpeedY * GameSettings.SkyCoeffY;
                var newLeft = left;

                if (State.Angles.X > 0) {
                    newLeft = left + State.SpeedX * GameSettings.SkyCoeffX;
                }
                else if (State.Angles.X < 0) {
                    newLeft = left - State.SpeedX * GameSettings.SkyCoeffX;
                }
                PositionInCanvas(skyImage, newLeft, newTop);
                CheckSkyOutOfBound(skyImage);
            }
        }

        // Update mountain
        private void UpdateMountain() {
            foreach (var mountainImage in _mountainImages) {
                var left = Canvas.GetLeft(mountainImage);
                var top = Canvas.GetTop(mountainImage);
                var newLeft = left;

                if (State.Angles.X > 0) {
                    newLeft = left + State.SpeedX * GameSettings.MountainCoeffX;
                }
                else if (State.Angles.X < 0) {
                    newLeft = left - State.SpeedX * GameSettings.MountainCoeffX;
                }
                PositionInCanvas(mountainImage, newLeft, top);
                CheckMountainOutOfBound(mountainImage);
            }
        }

        // Update score
        private void UpdateScore() {
            var positionDelta = Math.Sqrt(Math.Pow(Math.Abs(State.SpeedX), 2) + Math.Pow(Math.Abs(State.SpeedY), 2));
            State.Score += positionDelta;
        }

        #endregion

        #region Calculations

        // Calculate SpeedX
        private void CalculateSpeedX() {
            var x = Math.Abs(State.Angles.X);
            var value = GameSettings.MaxSpeedX / 60 * x;
            var limitedValue = value < GameSettings.MaxSpeedX ? value : GameSettings.MaxSpeedX;
            State.SpeedX = Math.Round(limitedValue * State.SpeedY / GameSettings.MaxSpeedY, 2);
        }

        // Calculate SpeedY
        private void CalculateSpeedY() {
            var x = State.Angles.Z > GameSettings.VerticalTolerance || State.Angles.Z < -GameSettings.VerticalTolerance ? State.Angles.Z : 0;
            var delta = Math.Pow(Math.E, .002 * x) - 1;
            var newSpeed = Math.Round(State.SpeedY + delta, 2);
            State.SpeedY = newSpeed > GameSettings.MaxSpeedY ? GameSettings.MaxSpeedY : newSpeed;
        }

        #endregion

        #region Initialization

        // Call init methods
        private async Task InitWindow() {
            await InitMedia();
            PauseWindow.Visibility = Visibility.Collapsed;
            UpdateWindow.Visibility = Visibility.Collapsed;
            Blackscreen.Opacity = 1;
            InitStoryboards();
            InitCanvas();
            InitPlane();
            InitGround();
            InitSky();
            InitMountains();
        }

        private async Task InitMedia() {
            _mediaElements = new MediaElement[3];
            _mediaElements[0] = await MyHelpers.LoadSoundFile(@"Assets\MySounds\JetSound.mp3");
            _mediaElements[1] = await MyHelpers.LoadSoundFile(@"Assets\MySounds\StarTheme.mp3");
            _mediaElements[2] = await MyHelpers.LoadSoundFile(@"Assets\MySounds\CrowdedAmbient.mp3");

            foreach (var mediaElement in _mediaElements) {
                UpdateWindow.Children.Add(mediaElement);
                mediaElement.MediaOpened += (sender, args) => {
                    var element = sender as MediaElement;
                    if (State.IsRunning) {
                        if (element != null && (element.Name == "JetSound" || element.Name == "StarTheme"))
                            element.Play();
                    }
                    else {
                        if (element != null && element.Name == "CrowdedAmbient")
                            element.Play();
                    }
                };
                mediaElement.IsMuted = _app.GameSettings.SoundMuted;
                mediaElement.IsLooping = true;
                mediaElement.AutoPlay = false;
            }
            _mediaElements[0].Volume = .3;
            _mediaElements[0].Name = "JetSound";
            _mediaElements[1].Volume = .4;
            _mediaElements[1].Name = "StarTheme";
            _mediaElements[2].Volume = .4;
            _mediaElements[2].Name = "CrowdedAmbient";
        }

        // Add EventHandlers to storyboards
        private void InitStoryboards() {
            EventHandler<object> fadeInEventHandler = (sender, o) => {
                if (!State.IsRunning) {
                    State.ResetSpeeds().ResetAngles();
                    UpdateWindow.Visibility = Visibility.Collapsed;
                    PauseWindow.Visibility = Visibility.Visible;
                    _mediaElements[0].Stop();
                    _mediaElements[1].Stop();
                    _mediaElements[2].Play();
                }
                else {
                    State.GetNextLocation();
                    UpdateWindow.Visibility = Visibility.Visible;
                    PauseWindow.Visibility = Visibility.Collapsed;
                    _mediaElements[0].Play();
                    _mediaElements[1].Play();
                    _mediaElements[2].Stop();
                }
                FadeOutInitialBlackscreenStoryboard.Begin();
            };

            FadeInInitialBlackscreenStoryboard.Completed += fadeInEventHandler;
        }

        // Set plane position on canvas
        private void InitPlane() {
            var left = (PlaneArea.ActualWidth - Airplane.ActualWidth) * 0.5;
            Canvas.SetLeft(Airplane, left);

            var top = (PlaneArea.ActualHeight - Airplane.ActualHeight) * 0.75;
            Canvas.SetTop(Airplane, top);
        }

        // Set canvas sizes and add projections for 3D look
        private void InitCanvas() {
            var groundProjection = new PlaneProjection {
                RotationX = -86,
                GlobalOffsetY = 10,
                GlobalOffsetZ = 320
            };
            GroundDrawArea.Projection = groundProjection;
            GroundDrawArea.Height = This.ActualHeight;
            GroundDrawArea.Width = This.ActualWidth;
            SkyBox.Clip = new RectangleGeometry {
                Rect = new Rect {
                    X = 0,
                    Y = 0,
                    Height = This.ActualHeight * 0.5,
                    Width = This.ActualWidth
                }
            };
            var skyProjection = new PlaneProjection {
                RotationX = 55,
                GlobalOffsetY = -50,
                GlobalOffsetZ = 150
            };
            SkyDrawArea.Projection = skyProjection;
            SkyDrawArea.Height = This.ActualHeight;
            SkyDrawArea.Width = This.ActualWidth;
            MountainDrawArea.Height = This.ActualHeight * 0.5;
            MountainDrawArea.Width = This.ActualWidth;
        }

        // Fill _groundImages array with images and initialize them in canvas
        private void InitGround() {
            var width = This.ActualWidth;
            var image = new BitmapImage(new Uri(BaseUri, "/Assets/MyImages/ground.png"));
            _groundImages = new[] {
                new Image() {Width = width, Height = width, Source = image},
                new Image() {Width = width, Height = width, Source = image},
                new Image() {Width = width, Height = width, Source = image},
                new Image() {Width = width, Height = width, Source = image},
            };
            var i = 0;
            foreach (var backgroundImage in _groundImages) {
                GroundDrawArea.Children.Add(backgroundImage);
                Canvas.SetZIndex(backgroundImage, -99);
                SetInitialGroundPosition(i);
                i++;
            }
        }

        // Fill _skyImages array with images and initialize them in canvas
        private void InitSky() {
            var image = new BitmapImage(new Uri(BaseUri, "/Assets/MyImages/big-sky.png"));
            const int height = 270;
            const int width = 1728;
            _skyImages = new[] {
                new Image() {Height = height, Width = width, Source = image},
                new Image() {Height = height, Width = width, Source = image},
                new Image() {Height = height, Width = width, Source = image},
                new Image() {Height = height, Width = width, Source = image},
            };
            var i = 0;
            foreach (var skyImage in _skyImages) {
                SkyDrawArea.Children.Add(skyImage);
                Canvas.SetZIndex(skyImage, -90);
                SetInitialSkyPosition(i);
                i++;
            }
        }

        // Fill _mountainImages array with images and initialize them in canvas
        private void InitMountains() {
            var image = new BitmapImage(new Uri(BaseUri, "/Assets/MyImages/mountains.png"));
            const double height = 110 * 0.5;
            const double width = 2098 * 0.5;
            _mountainImages = new[] {
                new Image() {Height = height, Width = width, Source = image},
                new Image() {Height = height, Width = width, Source = image}
            };
            var i = 0;
            foreach (var mountainImage in _mountainImages) {
                MountainDrawArea.Children.Add(mountainImage);
                Canvas.SetZIndex(mountainImage, -80);
                SetInitialMountainPosition(i);
                i++;
            }
        }

        // Initial mountain image positioning
        private void SetInitialMountainPosition(int index) {
            double x, y;
            var areaHeight = MountainDrawArea.ActualHeight;
            var areaWidth = MountainDrawArea.ActualWidth;
            var width = _mountainImages[index].Width;
            var height = _mountainImages[index].Height;
            switch (index) {
                case 0:
                    x = areaWidth * 0.5;
                    y = areaHeight - height;
                    break;
                case 1:
                    x = areaWidth * 0.5 - width + 1;
                    y = areaHeight - height;
                    break;
                default:
                    x = 0;
                    y = 0;
                    break;
            }
            PositionInCanvas(_mountainImages[index], x, y);
        }

        // Initial ground image positioning
        private void SetInitialGroundPosition(int index) {
            double x, y;
            var width = GroundDrawArea.ActualWidth;
            var height = GroundDrawArea.ActualHeight;
            switch (index) {
                case 0:
                    x = -width * 0.5 + 1;
                    y = -width + height * 0.5;
                    break;
                case 1:
                    x = width * 0.5;
                    y = -width + height * 0.5;
                    break;
                case 2:
                    x = -width * 0.5 + 1;
                    y = height * 0.5;
                    break;
                case 3:
                    x = width * 0.5;
                    y = height * 0.5;
                    break;
                default:
                    x = 0;
                    y = 0;
                    break;
            }
            PositionInCanvas(_groundImages[index], x, y);
        }

        // Initial sky image positioning
        private void SetInitialSkyPosition(int index) {
            double x, y;
            var areaHeight = SkyDrawArea.ActualHeight;
            var areaWidth = SkyDrawArea.ActualWidth;
            var width = _skyImages[index].ActualWidth;
            var height = _skyImages[index].ActualHeight;
            switch (index) {
                case 0:
                    x = -width + areaWidth * 0.5 + 1;
                    y = -height + areaHeight * 0.5 + 1;
                    break;
                case 1:
                    x = areaWidth * 0.5;
                    y = -height + areaHeight * 0.5 + 1;
                    break;
                case 2:
                    x = -width + areaWidth * 0.5 + 1;
                    y = areaHeight * 0.5;
                    break;
                case 3:
                    x = areaWidth * 0.5;
                    y = areaHeight * 0.5;
                    break;
                default:
                    x = 0;
                    y = 0;
                    break;
            }
            PositionInCanvas(_skyImages[index], x, y);
        }

        #endregion

        #region BoundChecks

        // Check if ground image is out of bounds and move to opposite side of canvas
        private static void CheckGroundOutOfBound(Image groundImage) {
            var width = groundImage.Width;
            var height = groundImage.Height;

            var top = Canvas.GetTop(groundImage);
            var left = Canvas.GetLeft(groundImage);

            if (left > width) {
                Canvas.SetLeft(groundImage, left - 2 * width + 2);
            }
            else if (left < -width) {
                Canvas.SetLeft(groundImage, left + 2 * width - 2);
            }

            if (top > height) {
                Canvas.SetTop(groundImage, top - 2 * height + 2);
            }
            else if (top < -height) {
                Canvas.SetTop(groundImage, top + 2 * height - 2);
            }
        }

        // Check if sky image is out of bounds and move to opposite side of canvas
        private static void CheckSkyOutOfBound(Image skyImage) {
            var width = skyImage.Width;
            var height = skyImage.Height;
            var left = Canvas.GetLeft(skyImage);
            var top = Canvas.GetTop(skyImage);

            if (left > width) {
                Canvas.SetLeft(skyImage, left - 2 * width + 2);
            }
            else if (left < -width) {
                Canvas.SetLeft(skyImage, left + 2 * width - 2);
            }

            if (top > height) {
                Canvas.SetTop(skyImage, top - 2 * height + 2);
            }
            else if (top < -height) {
                Canvas.SetTop(skyImage, top + 2 * height - 2);
            }
        }

        // Check if mountain image is out of bounds and move to opposite side of canvas
        private static void CheckMountainOutOfBound(Image mountainImage) {
            var width = mountainImage.Width;
            var left = Canvas.GetLeft(mountainImage);

            if (left > width) {
                Canvas.SetLeft(mountainImage, left - 2 * width + 2);
            }
            else if (left < -width) {
                Canvas.SetLeft(mountainImage, left + 2 * width - 2);
            }
        }

        #endregion

        #region Statics

        // Position UIElement on canvas
        private static void PositionInCanvas(UIElement element, double x, double y) {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
        }

        #endregion

        #region PropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        #endregion
    }
}