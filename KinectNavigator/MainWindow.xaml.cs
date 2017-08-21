using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Controls;
using System.ComponentModel;
using Microsoft.Maps.MapControl.WPF;
using System.Xml;
using System.Net;
using System.Globalization;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit;
using Microsoft.Kinect.Toolkit.Interaction;
using Microsoft.Kinect.Toolkit.Controls;
using System.Configuration;

namespace KinectNavigator
{

    public class DummyInteractionClient : IInteractionClient
    {
        public InteractionInfo GetInteractionInfoAtLocation(
            int s,
            InteractionHandType h,
            double x,
            double y
        )
        {
            var r = new InteractionInfo();
            r.IsGripTarget = true;
            r.IsPressTarget = true;
            r.PressAttractionPointX = 0.5;
            r.PressAttractionPointY = 0.5;
            r.PressTargetControlId = 1;
            return r;
        }
    }
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private MapLayer pinLayer;
        private MapLayer routeLayer;

        private string BingMapsKey = ConfigurationManager.AppSettings["BingMapsAPI"];
        string Labels = "ABCDEFGHIJKLMNOPQRSTUVWXYZαβγδεζηθικλμνξοπρστυφχψω";

        private bool grabbedRightEh;
        private bool grabbedLeftEh;

        private Vector startR;
        private Vector startL;
        private Vector currentR;
        private Vector currentL;
        private Vector shoulder;

        private Ellipse leftCursor;
        private Ellipse rightCursor;
        private SolidColorBrush inactive;
        private SolidColorBrush active;

        private Location startLocation;

        private int pushPinNo;

        private KinectSensor ks;
        private Skeleton[] skels;
        private UserInfo[] user_infos;

        private InteractionStream inter_stream;
        private Speech speech;

        private bool zoomEnabled = false;
        private bool rotateEnabled = false;
        private bool movementEnabled = false;
        private double startHeading;
        private double startZoom;

        private string _StatusBarText;
        public string StatusBarText { get { return _StatusBarText; } set { _StatusBarText = value; NotifyPropertyChanged("StatusBarText"); } }

        // private readonly KinectSensorChooser sensorChooser;

        public MainWindow()
        {
            /* Basic map logic */
            InitializeComponent();
            mainMap.CredentialsProvider = new ApplicationIdCredentialsProvider(BingMapsKey);
            mainMap.Focus();
            var rmm = new RoadMode();
            mainMap.Mode = rmm;
            pinLayer = new MapLayer();
            pinLayer.Name = "PinLayer";
            mainMap.Children.Add(pinLayer);
            routeLayer = new MapLayer();
            routeLayer.Name = "RouteLayer";
            mainMap.Children.Add(routeLayer);
            mainMap.KeyUp += new KeyEventHandler(Keyboard_Tester);
            pushPinNo = 0;

            /* Speech Recognition */
            speech = new Speech();
            speech.OnParsedEvent += ReciveSpeechEvents;
            speech.StartButton_Click(null, null);
            speech.PropertyChanged += new PropertyChangedEventHandler(speechPropertyChanged);
            
            /* Kinect Setup and Hand Cursors */
            InitKinect();
            shoulder = new Vector(0.75, 0);
   
            inactive = new SolidColorBrush(Colors.SteelBlue);
            inactive.Opacity = 0.5;
            active = new SolidColorBrush(Colors.Tomato);
            active.Opacity = 0.5;
            Style style = Application.Current.FindResource("FlashingCursorStyle") as Style;
            leftCursor = new Ellipse();
            leftCursor.Fill = inactive;
            leftCursor.Width = 60;
            leftCursor.Height = 60;
            leftCursor.Visibility = Visibility.Hidden;
            leftCursor.Style = style;
            rightCursor = new Ellipse();
            rightCursor.Fill = active;
            rightCursor.Width = 60;
            rightCursor.Height = 60;
            rightCursor.Visibility = Visibility.Hidden;
            rightCursor.Style = style;
            kinectCanvas.Children.Add(leftCursor);
            kinectCanvas.Children.Add(rightCursor);
            Ellipse el = new Ellipse();
            el.Height = 100;
            el.Width = 100;
            el.Visibility = Visibility.Visible;
            el.Fill = active;
            // double cy = (820 - el.Height - 200) / 2.0;
            // double cx = (1600 - el.Width - 200) / 2.0;
            double cy = (this.ActualHeight - leftCursor.Height) / 2;
            double cx = (this.ActualWidth - leftCursor.Width - this.ActualWidth / 4) / 2;
            cy += 400;
            cx += 1200;
            el.SetValue(Canvas.LeftProperty, cx);
            el.SetValue(Canvas.TopProperty, cy);
            // kinectCanvas.Children.Add(el);


            StatusBarText = "Use both hands";
            Statusbar.DataContext = this;
        }


        private void moveCursor(Ellipse cursor, double dx, double dy)
        {
            cursor.SetValue(Canvas.TopProperty, (double)rightCursor.GetValue(Canvas.TopProperty) + dy);
            cursor.SetValue(Canvas.LeftProperty, (double)rightCursor.GetValue(Canvas.LeftProperty) + dy);
        }

        private void positionCursor(Ellipse cursor, double x, double y)
        {
            cursor.SetValue(Canvas.TopProperty, y);
            cursor.SetValue(Canvas.LeftProperty, x);
        }

        private void SensorChooserOnKinectChanged(object s, KinectChangedEventArgs args)
        {
            if (args.OldSensor != null)
            {
                try
                {
                    args.OldSensor.DepthStream.Range = DepthRange.Default;
                    args.OldSensor.SkeletonStream.EnableTrackingInNearRange = false;
                    args.OldSensor.DepthStream.Disable();
                    args.OldSensor.SkeletonStream.Disable();
                }
                catch (InvalidOperationException)
                {
                    // wtf
                }
            }
            if (args.NewSensor != null)
            {
                try
                {
                    args.NewSensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                    args.NewSensor.SkeletonStream.Enable();
                    args.NewSensor.DepthStream.Range = DepthRange.Default;
                    args.NewSensor.SkeletonStream.EnableTrackingInNearRange = false;
                }
                catch (InvalidOperationException)
                {
                    // truly wtf
                }
            }
        }
        private void speechPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            this.StatusBarText = speech.speechSniplets;
            this.Statusbar.Foreground = Brushes.White;
        }

        private void InitKinect()
        {
            ks = KinectSensor.KinectSensors.FirstOrDefault();
            if (ks == null)
                throw new Exception("No Sensor detected");

            ks.Start();
            //ks = this.sensorChooser.Kinect;

            skels = new Skeleton[ks.SkeletonStream.FrameSkeletonArrayLength];
            user_infos = new UserInfo[InteractionFrame.UserInfoArrayLength];

            ks.DepthStream.Range = DepthRange.Default;
            ks.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
            ks.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
            // ks.SkeletonStream.EnableTrackingInNearRange = true;
            ks.SkeletonStream.Enable();
            inter_stream = new InteractionStream(ks, new DummyInteractionClient());
            inter_stream.InteractionFrameReady += this.OnInteractionFrameReady;

            ks.ColorStream.Disable();

            ks.DepthFrameReady += OnDepthFrameReady;
            ks.SkeletonFrameReady += OnSkeletonFrameReady;
        }

        private Dictionary<int, InteractionHandEventType> _lastLeftHandEvents = new Dictionary<int, InteractionHandEventType>();
        private Dictionary<int, InteractionHandEventType> _lastRightHandEvents = new Dictionary<int, InteractionHandEventType>();
        private void OnInteractionFrameReady(object o, InteractionFrameReadyEventArgs e)
        {
            using (var iaf = e.OpenInteractionFrame()) //dispose as soon as possible
            {
                if (iaf == null)
                    return;
                iaf.CopyInteractionDataTo(user_infos);
            }
            foreach (var u in user_infos)
            {
                var userId = u.SkeletonTrackingId;
                if (userId == 0)
                    continue;
                var hands = u.HandPointers;
                if (hands.Count == 0)
                    Console.WriteLine("No hands.");
                else
                {
                    foreach (var hand in hands)
                    {
                        double cy = (this.ActualHeight - leftCursor.Height) / 2;
                        double cx = (this.ActualWidth - leftCursor.Width - this.ActualWidth / 4) / 2;
                        var v_hand = new Vector(hand.X, hand.Y);
                        if (hand.HandType == InteractionHandType.Right)
                        {
                            v_hand += shoulder;
                            rightCursor.SetValue(Canvas.LeftProperty, cx + v_hand.X * 420);
                            rightCursor.SetValue(Canvas.TopProperty, cy + v_hand.Y * 420);
                            if (!hand.IsTracked || hand.HandEventType == InteractionHandEventType.GripRelease)
                            {
                                startR = new Vector(0, 0);
                                currentR = new Vector(0, 0);
                                grabbedRightEh = false;
                                startLocation = null;
                                startHeading = mainMap.Heading;
                                rightCursor.Visibility = Visibility.Visible;
                                rightCursor.Fill = inactive;
                                if (!hand.IsTracked)
                                    rightCursor.Visibility = Visibility.Hidden;
                            }
                            else if (grabbedRightEh)
                            {
                                currentR = v_hand;
                            }
                            else if (hand.HandEventType == InteractionHandEventType.Grip)
                            {
                                startR = v_hand;
                                currentR = v_hand;
                                grabbedRightEh = true;
                                startLocation = mainMap.Center;
                                rightCursor.Fill = active;
                                rightCursor.Visibility = Visibility.Visible;
                            }
                        }

                        if (hand.HandType == InteractionHandType.Left)
                        {
                            v_hand -= shoulder;
                            leftCursor.SetValue(Canvas.TopProperty, cy + v_hand.Y * 420);
                            leftCursor.SetValue(Canvas.LeftProperty, cx + v_hand.X * 420);
                            if (!hand.IsTracked || hand.HandEventType == InteractionHandEventType.GripRelease)
                            {
                                startL = new Vector(0, 0);
                                currentL = new Vector(0, 0);
                                grabbedLeftEh = false;
                                startLocation = null;
                                startHeading = mainMap.Heading;
                                leftCursor.Visibility = Visibility.Visible;
                                leftCursor.Fill = inactive;
                                if (!hand.IsTracked)
                                    leftCursor.Visibility = Visibility.Hidden;
                            }
                            else if (grabbedLeftEh)
                            {
                                currentL = v_hand;
                            }
                            else if (hand.HandEventType == InteractionHandEventType.Grip)
                            {
                                startL = v_hand;
                                currentL = v_hand;
                                grabbedLeftEh = true;
                                startLocation = mainMap.Center;
                                startHeading = mainMap.Heading;
                                startZoom = mainMap.ZoomLevel;

                                leftCursor.Fill = active;
                                leftCursor.Visibility = Visibility.Visible;
                            }
                        }
                    }
                    if (grabbedLeftEh && grabbedRightEh)
                    {
                        Vector v_start = startL - startR;
                        Vector v_current = currentL - currentR;
                        Vector v_Rmove = currentR - startR;
                        Vector v_Lmove = currentL - startL;
                        Vector v_moveTotal = (v_Rmove + v_Lmove) / 2;

                        double newHeading = mainMap.Heading;
                        double lenChange = v_current.Length - v_start.Length;
                        double newZoomLevel = startZoom;
                        // StatusBarText = mainMap.ZoomLevel.ToString();
                        if (zoomEnabled)
                        {
                            newZoomLevel += lenChange;
                        }
                        if (newZoomLevel < 4.0)
                        {
                            newZoomLevel = 4.0;
                        }


                        double sa = 0.0, ca = 0.0;
                        ca = Math.Cos(-newHeading * (Math.PI / 180));
                        sa = Math.Sin(-newHeading * (Math.PI / 180));
                        if (Math.Abs(v_current.Length) > 0.4 && rotateEnabled)
                        {
                            newHeading = startHeading + Vector.AngleBetween(v_start, v_current);
                        }



                        if (movementEnabled)
                            v_moveTotal = new Vector(ca * v_moveTotal.X - sa * v_moveTotal.Y, sa * v_moveTotal.X + ca * v_moveTotal.Y);
                        else
                            v_moveTotal = new Vector(0, 0);

                        double mapFactor = 0.17 * (Math.Pow(2, 12 - newZoomLevel));
                        double longitude = startLocation.Longitude - (v_moveTotal.X * mapFactor);
                        double latitude = startLocation.Latitude + (v_moveTotal.Y * mapFactor * 0.5);
                        Console.WriteLine("Adjusting view");
                        mainMap.SetView(new Location(latitude, longitude), newZoomLevel, newHeading);
                    }
                }
            }
        }

        private double scalarVector(Vector v1, Vector v2)
        {
            double result;
            result = v1.X * v2.X + v1.Y * v2.Y;
            return result;
        }

        private void OnSkeletonFrameReady(object o, SkeletonFrameReadyEventArgs sf)
        {
            // Console.WriteLine("Skeleton");
            {
                using (SkeletonFrame skeletonFrame = sf.OpenSkeletonFrame())
                {
                    if (skeletonFrame == null)
                        return;
                    try
                    {
                        skeletonFrame.CopySkeletonDataTo(skels);
                        var accelerometerReading = ks.AccelerometerGetCurrentReading();
                        inter_stream.ProcessSkeleton(skels, accelerometerReading, skeletonFrame.Timestamp);
                    }
                    catch (InvalidOperationException)
                    {
                        Console.WriteLine("Skeleton Problems");
                        // SkeletonFrame functions may throw w
                        // into a bad state.  Ignore the frame in that case.
                    }
                }
            }
        }

        private void OnDepthFrameReady(object o, DepthImageFrameReadyEventArgs df)
        {
            // Console.WriteLine("DepthFrame");
            using (DepthImageFrame depthFrame = df.OpenDepthImageFrame())
            {
                if (depthFrame == null)
                    return;

                try
                {
                    inter_stream.ProcessDepth(depthFrame.GetRawPixelData(), depthFrame.Timestamp);
                }
                catch (InvalidOperationException)
                {
                    // depth frame weirdnesshen the sensor gets
                    Console.WriteLine("Depth Problems");
                }
            }
        }

        private void OnEventChanged(object o, KinectChangedEventArgs e)
        {
            e.NewSensor.AllFramesReady += OnAllFramesReady;
            Console.WriteLine("###########################################");
            Console.WriteLine("Found Kinect Sesnor");
            Console.WriteLine("###########################################");

        }

        private void OnAllFramesReady(object o, AllFramesReadyEventArgs e)
        {
            Console.WriteLine("TEST");
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            this.ks.Stop();
            Environment.Exit(0);
            Application.Current.Shutdown();
        }

        private void ReciveSpeechEvents(object o, SpeechEventArgs e)
        {
            switch (e.TheEvent.Kind)
            {
                case RecEventType.Reset:
                    Statusbar.Foreground = Brushes.PaleVioletRed;
                    statusBarBoard.Begin();
                    ClearRoute();
                    Clear_Pins();
                    break;
                case RecEventType.Goto:
                    Statusbar.Foreground = Brushes.LightBlue;
                    statusBarBoard.Begin();
                    Get_Location(null, e.TheEvent);
                    break;
                case RecEventType.RouteFromHereToThere:
                    Statusbar.Foreground = Brushes.LightBlue;
                    statusBarBoard.Begin();
                    Console.WriteLine("Routing A B");
                    RouteHereToThere(e.TheEvent.StreetA);

                    break;
                case RecEventType.RouteFromThereToHere:
                    Statusbar.Foreground = Brushes.LightBlue;
                    statusBarBoard.Begin();
                    Console.WriteLine("Routing A B");
                    RouteThereToHere(e.TheEvent.StreetA);
                    break;
                case RecEventType.RouteFromTo:
                    Statusbar.Foreground = Brushes.LightBlue;
                    statusBarBoard.Begin();
                    Console.WriteLine("Routing A B");
                    UpdateRoute(e.TheEvent.StreetA, e.TheEvent.StreetB);
                    break;
                case RecEventType.SetPin:
                    Statusbar.Foreground = Brushes.LightBlue;
                    statusBarBoard.Begin();
                    Center_Pin(null, e.TheEvent);
                    break;
                case RecEventType.DeletePin:
                    Statusbar.Foreground = Brushes.LightBlue;
                    statusBarBoard.Begin();
                    Remove_Label_Pin(null, e.TheEvent);
                    break;
                case RecEventType.SetZoon:
                    Statusbar.Foreground = Brushes.LightYellow;
                    statusBarBoard.Begin();
                    Console.Out.WriteLine(e.TheEvent.zoom.ToString());
                    mainMap.ZoomLevel = ((e.TheEvent.zoom / 100.0) * 17) + 4;
                    break;
                case RecEventType.Stop:
                    break;
                case RecEventType.EnableZoom:
                    Statusbar.Foreground = Brushes.LightGreen;
                    statusBarBoard.Begin();
                    zoomEnabled = true;
                    break;
                case RecEventType.DisableZoom:
                    Statusbar.Foreground = Brushes.LightPink;
                    statusBarBoard.Begin();
                    zoomEnabled = false;
                    break;
                case RecEventType.EnableRotation:
                    Statusbar.Foreground = Brushes.LightGreen;
                    statusBarBoard.Begin();
                    rotateEnabled = true;
                    break;
                case RecEventType.DisableRotation:
                    Statusbar.Foreground = Brushes.LightPink;
                    statusBarBoard.Begin();
                    rotateEnabled = false;
                    break;
                case RecEventType.EnableMovement:
                    Statusbar.Foreground = Brushes.LightGreen;
                    statusBarBoard.Begin();
                    movementEnabled = true;
                    break;
                case RecEventType.DisableMovement:
                    Statusbar.Foreground = Brushes.LightPink;
                    statusBarBoard.Begin();
                    movementEnabled = false;
                    break;
                case RecEventType.Circle:
                    StatusBarText = "Nope.";
                    Statusbar.Foreground = Brushes.PapayaWhip;
                    statusBarBoard.Begin();
                    //ks.ElevationAngle = 0;
                    //ks.ElevationAngle = 5;
                    //ks.ElevationAngle = 0;
                    break;

            }
        }

        private void Keyboard_Tester(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            switch (e.Key)
            {
                case Key.Enter:
                    Console.WriteLine("Executing G Command");
                    Center_Pin(null, null);
                    break;
                case Key.Space:
                    Clear_Pins(null, null);
                    break;
                case Key.Delete:
                    Remove_Last_Pin(null, null);
                    break;
                case Key.P:
                    this.RouteHereToThere("Paris");
                    break;
                /* case Key.Up:
                    ks.ElevationAngle = 5;
                    break;
                case Key.Down:
                    ks.ElevationAngle = 0;
                    break; */
                case Key.R:
                    rotateEnabled = rotateEnabled == true ? false : true;
                    break;
                case Key.Z:
                    zoomEnabled = zoomEnabled == true ? false : true;
                    break;
                case Key.M:
                    movementEnabled = movementEnabled == true ? false : true;
                    break;
                default:
                    Console.WriteLine(e.Key);
                    break;
            }
        }

        //Add a pushpin with a label to the map
        private Pushpin AddPushpinToMap(double latitude, double longitude, string pinLabel, MapLayer targetLayer)
        {
            Location location = new Location(latitude, longitude);
            Pushpin pushpin = new Pushpin();

            targetLayer.Children.Add(pushpin);
            pushpin.Content = pinLabel;
            pushpin.Location = location;
            return pushpin;
        }

        private Pushpin AddPushpinToMap(Location location, string pinLabel, MapLayer targetLayer)
        {
            return AddPushpinToMap(location.Latitude, location.Longitude, pinLabel, targetLayer);
        }

        private void Center_Pin(object sender, RecEvent e)
        {
            string label = "";
            if (e != null)
            {
                label = e.Label;
            }
            if (label.Equals(""))
            {
                label = Labels[pushPinNo].ToString();
                pushPinNo++;
            }
            Location pos = mainMap.Center;
            double lat = pos.Latitude;
            double lon = pos.Longitude;
            AddPushpinToMap(lat, lon, label, pinLayer);
        }

        // audio interaction methods
        private void Clear_Pins(object sender, RecEvent e)
        {
            var pushpins = pinLayer.Children.OfType<Pushpin>();
            foreach (Pushpin p in pushpins.ToList<Pushpin>())
            {
                pinLayer.Children.Remove(p);
            }
        }

        private void Clear_Pins()
        {
            pinLayer.Children.Clear();
        }

        private void Remove_Last_Pin(object sender, RecEvent e)
        {
            var pushpin = pinLayer.Children.OfType<Pushpin>();
            if (pushpin.Any())
            {
                pinLayer.Children.Remove(pushpin.Last<Pushpin>());
            }
        }

        private void Remove_Label_Pin(object sender, RecEvent e)
        {
            string rl = "";

            if (e == null)
            {
                // return;
            }

            // rl = e.Label;
            rl = "A";
            var pushpins = pinLayer.Children.OfType<Pushpin>();
            foreach (Pushpin p in pushpins.ToList<Pushpin>())
            {
                if (p.Content.Equals(rl))
                {
                    pinLayer.Children.Remove(p);
                    Console.WriteLine("Removed " + rl);
                    break;
                }
            }
        }

        // continue writing location
        private void Get_Location(object sender, RecEvent e)
        {
            string eTest = "Berlin";
            string rawQuery = "";
            if (e == null) rawQuery = eTest;
            else rawQuery = e.StreetA;
            if (rawQuery.Equals(""))
                return;
            string locationQuery = rawQuery.Replace(" ", "%20").Replace(",", "");
            XmlDocument searchResponse = Geocode(locationQuery);

            //Find and display points of interest near the specified location
            FindandDisplayNearbyPOI(searchResponse, locationQuery, pinLayer);
        }

        private Pushpin getLocationPushpin(string location, string label, MapLayer targetLayer)
        {
            string rawQuery = location;
            string locationQuery = rawQuery.Replace(" ", "%20").Replace(",", "");
            XmlDocument searchResponse = Geocode(locationQuery);

            //Find and display points of interest near the specified location
            return FindandDisplayNearbyPOI(searchResponse, label, targetLayer);

        }

        public XmlDocument Geocode(string addressQuery)
        {
            //Create REST Services geocode request using Locations API
            string geocodeRequest = "http://dev.virtualearth.net/REST/v1/Locations/" + addressQuery + "?o=xml&key=" + BingMapsKey;

            //Make the request and get the response
            XmlDocument geocodeResponse = GetXmlResponse(geocodeRequest);

            return (geocodeResponse);
        }

        private XmlDocument GetXmlResponse(string requestUrl)
        {
            System.Diagnostics.Trace.WriteLine("Request URL (XML): " + requestUrl);
            HttpWebRequest request = WebRequest.Create(requestUrl) as HttpWebRequest;
            using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new Exception(String.Format("Server error (HTTP {0}: {1}).",
                    response.StatusCode,
                    response.StatusDescription));
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(response.GetResponseStream());
                return xmlDoc;
            }
        }

        private Pushpin FindandDisplayNearbyPOI(XmlDocument xmlDoc, string label, MapLayer targetLayer)
        {
            //Get location information from geocode response 

            //Create namespace manager
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsmgr.AddNamespace("rest", "http://schemas.microsoft.com/search/local/ws/rest/v1");

            //Get all geocode locations in the response 
            XmlNodeList locationElements = xmlDoc.SelectNodes("//rest:Location", nsmgr);
            if (locationElements.Count == 0)
            {
                Console.WriteLine("The location you entered could not be geocoded.");
                return null;
            }
            else
            {
                //Get the geocode location points that are used for display (UsageType=Display)
                XmlNodeList displayGeocodePoints =
                        locationElements[0].SelectNodes(".//rest:GeocodePoint/rest:UsageType[.='Display']/parent::node()", nsmgr);
                string latitude = displayGeocodePoints[0].SelectSingleNode(".//rest:Latitude", nsmgr).InnerText;
                string longitude = displayGeocodePoints[0].SelectSingleNode(".//rest:Longitude", nsmgr).InnerText;
                //Center the map at the geocoded location and display the results
                double la = Double.Parse(latitude, CultureInfo.InvariantCulture);
                double lo = Double.Parse(longitude, CultureInfo.InvariantCulture);

                mainMap.Center = new Location(la, lo);
                
                mainMap.ZoomLevel = 12;
                Pushpin pin = AddPushpinToMap(Double.Parse(latitude, CultureInfo.InvariantCulture), Double.Parse(longitude, CultureInfo.InvariantCulture), label, targetLayer);
                return pin;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        private void UpdateRoute(string origin, string goal)
        {
            Console.WriteLine(origin);
            Console.WriteLine(goal);
            Pushpin a = getLocationPushpin(origin, "A", routeLayer);
            Pushpin b = getLocationPushpin(goal, "B", routeLayer);
            if (a == null || b == null)
            {
                StatusBarText = "Route could not be found.";
                statusBarBoard.Begin();
                return;
            }
            this.UpdateRoute(a, b);
        }

        private void RouteHereToThere(string goal)
        {
            Console.WriteLine(goal);
            Pushpin a = AddPushpinToMap(mainMap.Center, "A", routeLayer);
            Pushpin b = getLocationPushpin(goal, "B", routeLayer);
            if (a == null || b == null)
            {
                StatusBarText = "Route could not be found.";
                statusBarBoard.Begin();
                return;
            }
            this.UpdateRoute(a, b);

        }

        private void RouteThereToHere(string origin)
        {
            Console.WriteLine(origin);
            Pushpin b = AddPushpinToMap(mainMap.Center, "B", routeLayer);
            Pushpin a = getLocationPushpin(origin, "A", routeLayer);
            if (a == null || b == null)
            {
                StatusBarText = "Route could not be found.";
                statusBarBoard.Begin();
                return;
            }
            this.UpdateRoute(a, b);

        }

        private void ClearRoute()
        {
            routeLayer.Children.Clear();
        }
        private async void UpdateRoute(Pushpin StartPin, Pushpin EndPin)
        {
            // routeLayer.Children.Clear();
            var startCoord = LocationToCoordinate(StartPin.Location);
            var endCoord = LocationToCoordinate(EndPin.Location);

            Console.WriteLine(mainMap.ZoomLevel);
            //Calculate a route between the start and end pushpin. 
            var response = await BingMapsRESTToolkit.ServiceManager.GetResponseAsync(new BingMapsRESTToolkit.RouteRequest()
            {
                Waypoints = new List<BingMapsRESTToolkit.SimpleWaypoint>()
                {
                    new BingMapsRESTToolkit.SimpleWaypoint(startCoord),
                    new BingMapsRESTToolkit.SimpleWaypoint(endCoord)
                },
                BingMapsKey = BingMapsKey,
                RouteOptions = new BingMapsRESTToolkit.RouteOptions()
                {
                    RouteAttributes = new List<BingMapsRESTToolkit.RouteAttributeType>
                    { 
                        //Be sure to return the route path information so that we can draw the route line. 
                        BingMapsRESTToolkit.RouteAttributeType.RoutePath
                    }
                }
            });

            if (response != null &&
                response.ResourceSets != null &&
                response.ResourceSets.Length > 0 &&
                response.ResourceSets[0].Resources != null &&
                response.ResourceSets[0].Resources.Length > 0)
            {
                var route = response.ResourceSets[0].Resources[0] as BingMapsRESTToolkit.Route;

                //Generate a Polyline from the route path information. 
                var locs = new LocationCollection();

                for (var i = 0; i < route.RoutePath.Line.Coordinates.Length; i++)
                {
                    locs.Add(new Location(route.RoutePath.Line.Coordinates[i][0], route.RoutePath.Line.Coordinates[i][1]));
                }

                var routeLine = new MapPolyline()
                {
                    Locations = locs,
                    Stroke = new SolidColorBrush(Colors.DodgerBlue),
                    StrokeThickness = 3
                };

                routeLayer.Children.Add(routeLine);

                // mainMap.ZoomLevel = newZoom;
                // mainMap.Center = betw;
                mainMap.ZoomLevel = 8.0;
            } else
            {
                StatusBarText = "Route could not be found.";
                statusBarBoard.Begin();
                return;
            }
        }

        private BingMapsRESTToolkit.Coordinate LocationToCoordinate(Location loc)
        {
            return new BingMapsRESTToolkit.Coordinate(loc.Latitude, loc.Longitude);
        }
    }
}