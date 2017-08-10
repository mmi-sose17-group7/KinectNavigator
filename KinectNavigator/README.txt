= KinectNavigator
Microsoft Kinect based Bing Maps client with Speech input support.

The project can be imported into Visual Studio via the built-in Git functionality of the Team explorer.
== Dependencies
* Nuget Package Manager Dependencies - BingMapsRestToolkit, Microsoft.Maps.MapControl.WPF, Microsoft.ProjectOxford.SpeechRecognition(-x64/x86)
** some libraries are arch-dependent, if possible building against x64 should be preferred
* Microsoft.Kinect.Toolkit - Installable through the Kinect SDK

== Required API Keys - Add in App.config
* Bing Maps API Key - Required for Map Tiles, Location and Routing
* Microsoft Speech API Key - Required for Microsoft Speech Recognition Service

== Building and Running
* For direct testing, ensure that a Kinect is connected and recognized in the Windows System.