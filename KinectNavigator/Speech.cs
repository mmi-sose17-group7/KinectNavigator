using System;
using System.Text.RegularExpressions;
using Microsoft.CognitiveServices.SpeechRecognition;
using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Configuration;

namespace KinectNavigator
{
    public enum RecEventType
    {
        Stop,
        Reset,
        Goto,
        SetPin,
        SetZoon,
        RouteFromHereToThere,
        RouteFromThereToHere,
        RouteFromTo,
        DeletePin,
        EnableZoom,
        DisableZoom,
        DisableRotation,
        EnableMovement,
        EnableRotation,
        DisableMovement,
        Circle,
    }

    public class RecEvent
    {
        public RecEventType Kind;
        public string StreetA, StreetB, Label;
        public int zoom;
        public RecEvent(RecEventType t)
        {
            this.Kind = t;
        }
        public RecEvent(RecEventType t, string a, string b)
        {
            this.Kind = t;
            this.StreetA = a;
            this.StreetB = b;
        }
        public RecEvent(RecEventType t, string a)
        {
            // used for labeling of newly set pins
            this.Kind = t;
            this.Label = a;
        }
        public RecEvent(RecEventType t, int zoom)
        {
            this.Kind = t;
            this.zoom = zoom;
        }

    }

    public class SpeechEventArgs : EventArgs
    {
        public readonly RecEvent TheEvent;

        public SpeechEventArgs(RecEvent e)
        {
            TheEvent = e;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Speech : Window, INotifyPropertyChanged
    {
        public event EventHandler<SpeechEventArgs> OnParsedEvent;
        /// <summary>
        /// You can also put the primary key in app.config, instead of using UI.
        /// string subscriptionKey = ConfigurationManager.AppSettings["primaryKey"];
        /// </summary>
        private string subscriptionKey = ConfigurationManager.AppSettings["SpeechAPI"];

        private string _speechSniplets;
        public string speechSniplets
        {
            get
            {
                return _speechSniplets;
            }
            set
            {
                _speechSniplets = value;
                NotifyPropertyChanged("speechSniplets");
            }
        }

        /// <summary>
        /// The microphone client
        /// </summary>
        private MicrophoneRecognitionClient micClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public Speech()
        {
            this.Initialize();
        }

        #region Events

        /// <summary>
        /// Implement INotifyPropertyChanged interface
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        #endregion Events
        /// <summary>
        /// Gets or sets subscription key
        /// </summary>
        public string SubscriptionKey
        {
            get
            {
                return this.subscriptionKey;
            }

            set
            {
                this.subscriptionKey = value;
            }
        }

        /// <summary>
        /// Gets the current speech recognition mode.
        /// </summary>
        /// <value>
        /// The speech recognition mode.
        /// </value>
        private SpeechRecognitionMode Mode
        {
            get
            {
                return SpeechRecognitionMode.ShortPhrase;
            }
        }

        /// <summary>
        /// Gets the default locale.
        /// </summary>
        /// <value>
        /// The default locale.
        /// </value>
        private string DefaultLocale
        {
            get { return "en-GB"; }
        }


        /// <summary>
        /// Gets the Cognitive Service Authentication Uri.
        /// </summary>
        /// <value>
        /// The Cognitive Service Authentication Uri.  Empty if the global default is to be used.
        /// </value>
        private string AuthenticationUri
        {
            get
            {
                return "";
            }
        }

        /// <summary>
        /// Initializes a fresh audio session.
        /// </summary>
        private void Initialize()
        {
            CreateMicrophoneRecoClient();
        }

        /// <summary>
        /// Handles the Click event of the _startButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="RoutedEventArgs"/> instance containing the event data.</param>
        public void StartButton_Click(object sender, RoutedEventArgs e)
        {
            this.micClient.StartMicAndRecognition();
        }

        /// <summary>
        /// Creates a new microphone reco client without LUIS intent support.
        /// </summary>
        private void CreateMicrophoneRecoClient()
        {
            this.micClient = SpeechRecognitionServiceFactory.CreateMicrophoneClient(
                SpeechRecognitionMode.ShortPhrase,
                this.DefaultLocale,
                this.SubscriptionKey);
            this.micClient.AuthenticationUri = this.AuthenticationUri;

            // Event handlers for speech recognition results
            this.micClient.OnMicrophoneStatus += this.OnMicrophoneStatus;
            this.micClient.OnPartialResponseReceived += this.OnPartialResponseReceivedHandler;
            this.micClient.OnResponseReceived += this.OnMicShortPhraseResponseReceivedHandler;
            this.micClient.OnConversationError += this.OnConversationErrorHandler;
        }

        /// <summary>
        /// Called when a final response is received;
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="SpeechResponseEventArgs"/> instance containing the event data.</param>
        private void OnMicShortPhraseResponseReceivedHandler(object sender, SpeechResponseEventArgs e)
        {

            Dispatcher.Invoke((Action)(() =>
            {
                // we got the final result, so it we can end the mic reco.  No need to do this
                // for dataReco, since we already called endAudio() on it as soon as we were done
                // sending all the data.
                this.micClient.EndMicAndRecognition();
                this.micClient.StartMicAndRecognition();
                this.WriteResponseResult(e);
            }));
        }

        /// <summary>
        /// Writes the response result.
        /// </summary>
        /// <param name="e">The <see cref="SpeechResponseEventArgs"/> instance containing the event data.</param>
        private void WriteResponseResult(SpeechResponseEventArgs e)
        {
            if (e.PhraseResponse.Results.Length == 0)
            {
                return;
            }

            for (int i = 0; i < e.PhraseResponse.Results.Length; i++)
            {

                RecEvent q = this.ParseRecEvent(e.PhraseResponse.Results[i].DisplayText);
                if (q != null)
                {
                    if (q.Kind == RecEventType.SetPin && e.PhraseResponse.Results[i].Confidence != Confidence.High) continue;
                    dispatchEvent(q);
                    if (q.Kind != RecEventType.DeletePin) break;
                }
            }
        }



        /// <summary>
        /// Called when a partial response is received.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="PartialSpeechResponseEventArgs"/> instance containing the event data.</param>
        private void OnPartialResponseReceivedHandler(object sender, PartialSpeechResponseEventArgs e)
        {
            Dispatcher.Invoke(
                (Action)(() =>
                {
                    this.speechSniplets = e.PartialResult;
                    RecEvent q = this.ParseRecEvent(e.PartialResult);
                    if (q != null && (q.Kind == RecEventType.Stop || q.Kind == RecEventType.Reset))
                    {
                            // we got the final result, so it we can end the mic reco.  No need to do this
                            // for dataReco, since we already called endAudio() on it as soon as we were done
                            // sending all the data.

                            this.micClient.EndMicAndRecognition();
                        dispatchEvent(q);
                    }

                }));
        }

        private void dispatchEvent(RecEvent e)
        {
            OnParsedEvent(new object(), new SpeechEventArgs(e));
        }

        private Regex stopRE = new Regex(@".{0,10}stop.*", RegexOptions.IgnoreCase);
        private Regex resetRE = new Regex(@".{0,10}reset.*", RegexOptions.IgnoreCase);
        private Regex gotoRE = new Regex(@".{0,10}go to (.+).", RegexOptions.IgnoreCase);
        private Regex circleRE = new Regex(@".{0,10}circle(.*)", RegexOptions.IgnoreCase);
        private Regex setPinRE = new Regex(@".{0,10}location.?(.*).", RegexOptions.IgnoreCase);
        private Regex enableZoomRE = new Regex(@".{0,10}enable .oo..", RegexOptions.IgnoreCase);
        private Regex disableZoomRE = new Regex(@".{0,10}disable .oo..", RegexOptions.IgnoreCase);
        private Regex fromToRE = new Regex(@".{0,10}go from (.+) to (.+).", RegexOptions.IgnoreCase);
        private Regex fromXtohereRE = new Regex(@".{0,10}from (.+) to here", RegexOptions.IgnoreCase);
        private Regex enableMovementRE = new Regex(@".{0,10}enable mov(.*)", RegexOptions.IgnoreCase);
        private Regex fromheretoXRE = new Regex(@".{0,10}from here to (.+).", RegexOptions.IgnoreCase);
        private Regex enableRotationRE = new Regex(@".{0,10}enable rota(.*)", RegexOptions.IgnoreCase);
        private Regex disableMovementRE = new Regex(@".{0,10}disable mov(.*)", RegexOptions.IgnoreCase);
        private Regex deletePinRE = new Regex(@".{0,10}delete location (.+).", RegexOptions.IgnoreCase);
        private Regex disableRotationRE = new Regex(@".{0,10}disable rota(.*)", RegexOptions.IgnoreCase);
        private Regex setZoomToXRE = new Regex(@".{0,10}.oo. i?n?\D?t?o?\D?(\d+).?(%|percent).*", RegexOptions.IgnoreCase);
        private RecEvent ParseRecEvent(string text)
        {
            if (stopRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.Stop);
            }

            if (resetRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.Reset);
            }

            if (gotoRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.Goto, gotoRE.Match(text).Groups[1].Value, "");
            }

            if (deletePinRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.DeletePin, deletePinRE.Match(text).Groups[1].Value);
            }

            if (setPinRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.SetPin, setPinRE.Match(text).Groups[1].Value);
            }

            if (setZoomToXRE.IsMatch(text))
            {
                int numVal = -1;
                try
                {
                    numVal = Convert.ToInt32(setZoomToXRE.Match(text).Groups[1].Value);
                }
                catch (FormatException _)
                {
                    return null;
                }
                if (numVal < 0 || numVal > 100) return null;
                WriteLine("Matched zoom regex {0}", numVal);
                return new RecEvent(RecEventType.SetZoon, numVal);
            }


            if (fromheretoXRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.RouteFromHereToThere, fromheretoXRE.Match(text).Groups[1].Value, "");
            }

            if (fromXtohereRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.RouteFromHereToThere, fromXtohereRE.Match(text).Groups[1].Value, "");
            }

            if (fromToRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.RouteFromTo, fromToRE.Match(text).Groups[1].Value, fromToRE.Match(text).Groups[2].Value);
            }

            if (disableZoomRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.DisableZoom);
            }
            if (enableZoomRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.EnableZoom);
            }

            if (disableMovementRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.DisableMovement);
            }
            if (enableMovementRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.EnableMovement);
            }

            if (disableRotationRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.DisableRotation);
            }
            if (enableRotationRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.EnableRotation);
            }

            if (circleRE.IsMatch(text))
            {
                return new RecEvent(RecEventType.Circle);
            }

            return null;
        }


        /// <summary>
        /// Called when an error is received.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="SpeechErrorEventArgs"/> instance containing the event data.</param>
        private void OnConversationErrorHandler(object sender, SpeechErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
            });

            this.WriteLine("--- Error received by OnConversationErrorHandler() ---");
            this.WriteLine("Error code: {0}", e.SpeechErrorCode.ToString());
            this.WriteLine("Error text: {0}", e.SpeechErrorText);
        }

        /// <summary>
        /// Called when the microphone status has changed.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="MicrophoneEventArgs"/> instance containing the event data.</param>
        private void OnMicrophoneStatus(object sender, MicrophoneEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Recording)
                {
                }
            });
        }

        /// <summary>
        /// Writes the line.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="args">The arguments.</param>
        private void WriteLine(string format, params object[] args)
        {
            var formattedStr = string.Format(format, args);
            Trace.WriteLine(formattedStr);
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine(formattedStr + "\n");
            });
        }

    }
}
