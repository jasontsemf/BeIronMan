using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.Media;

using Microsoft.Kinect;
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;

using GestureRecognizer;

using System.IO;
using System.Windows.Threading;

namespace W8_AugmentedReality
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private KinectSensor sensor;

        private RecognizerInfo kinectRecognizerInfo;
        private SpeechRecognitionEngine recognizer;

        private GestureRecognitionEngine recognitionEngine;

        private Skeleton[] skeletons = null;
        private JointType[] bones = { 
                                      // torso 
                                      JointType.Head, JointType.ShoulderCenter,
                                      JointType.ShoulderCenter, JointType.ShoulderLeft,
                                      JointType.ShoulderCenter, JointType.ShoulderRight,
                                      JointType.ShoulderCenter, JointType.Spine, 
                                      JointType.Spine, JointType.HipCenter,
                                      JointType.HipCenter, JointType.HipLeft, 
                                      JointType.HipCenter, JointType.HipRight,
                                      // left arm 
                                      JointType.ShoulderLeft, JointType.ElbowLeft,
                                      JointType.ElbowLeft, JointType.WristLeft,
                                      JointType.WristLeft, JointType.HandLeft,
                                      // right arm 
                                      JointType.ShoulderRight, JointType.ElbowRight,
                                      JointType.ElbowRight, JointType.WristRight,
                                      JointType.WristRight, JointType.HandRight,
                                      // left leg
                                      JointType.HipLeft, JointType.KneeLeft,
                                      JointType.KneeLeft, JointType.AnkleLeft,
                                      JointType.AnkleLeft, JointType.FootLeft,
                                      // right leg
                                      JointType.HipRight, JointType.KneeRight,
                                      JointType.KneeRight, JointType.AnkleRight,
                                      JointType.AnkleRight, JointType.FootRight,
                                    };

        private DrawingGroup drawingGroup; // Drawing group for skeleton rendering output
        private DrawingImage drawingImg; // Drawing image that we will display

        private byte[] colorData = null;
        private WriteableBitmap colorImageBitmap = null;

        private BitmapImage torso;
        private BitmapImage helmet;
        private BitmapImage rightUpperArm;
        private BitmapImage bug;
        private BitmapImage rightThigh;
        private BitmapImage rightCalf;
        private BitmapImage leftThigh;
        private BitmapImage leftCalf;

        SoundPlayer suitUpSound = new SoundPlayer("suitup.wav");
        private Boolean suitUpPlaying = false;

        SoundPlayer flySound = new SoundPlayer("flysound.wav");
        private Boolean flyPlaying = false;

        SoundPlayer serviceSound  = new SoundPlayer("jarvis.wav");
        private Boolean servicePlaying = false;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PopulatePoseLibrary();
            LoadImages();
            blackImg.Visibility = System.Windows.Visibility.Hidden;
            upRightArm.Visibility = System.Windows.Visibility.Hidden;
            upLeftArm.Visibility = System.Windows.Visibility.Hidden;
            lowRightArm.Visibility = System.Windows.Visibility.Hidden;
            lowLeftArm.Visibility = System.Windows.Visibility.Hidden;

            beamAngleTxt.Visibility = System.Windows.Visibility.Hidden;
            soundSourceAngleTxt.Visibility = System.Windows.Visibility.Hidden;
            recognizedColorTxt.Visibility = System.Windows.Visibility.Hidden;
            TBCountDown.Visibility = System.Windows.Visibility.Hidden;


            hud.Visibility = System.Windows.Visibility.Hidden;
            hudRed.Visibility = System.Windows.Visibility.Hidden;

            if (KinectSensor.KinectSensors.Count == 0)
            {
                MessageBox.Show("No Kinects detected", "Depth Sensor Basics");
                Application.Current.Shutdown();
            }
            else
            {
                sensor = KinectSensor.KinectSensors[0];
                if (sensor == null)
                {
                    MessageBox.Show("Kinect is not ready to use", "Depth Sensor Basics");
                    Application.Current.Shutdown();
                }
            }

            // -------------------------------------------------------
            // color 
            sensor.ColorStream.Enable();
            // allocate storage for color data 
            colorData = new byte[sensor.ColorStream.FramePixelDataLength];

            // create an empty bitmap with the same size as color frame 
            colorImageBitmap = new WriteableBitmap(
                      sensor.ColorStream.FrameWidth, sensor.ColorStream.FrameHeight,
                      96, 96, PixelFormats.Bgr32, null);
            colorImg.Source = colorImageBitmap;
            // register an event handler 
            sensor.ColorFrameReady += new EventHandler<ColorImageFrameReadyEventArgs>(sensor_ColorFrameReady);

            // skeleton stream 
            sensor.SkeletonStream.Enable();
            sensor.SkeletonFrameReady += new EventHandler<SkeletonFrameReadyEventArgs>(sensor_SkeletonFrameReady);
            skeletons = new Skeleton[sensor.SkeletonStream.FrameSkeletonArrayLength];

            // -------------------------------------------------------
            // Create the drawing group we'll use for drawing
            drawingGroup = new DrawingGroup();
            // Create an image source that we can use in our image control
            drawingImg = new DrawingImage(drawingGroup);
            // Display the drawing using our image control
            skeletonImg.Source = drawingImg;
            // prevent drawing outside of our render area
            drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, 640, 480));

            // start the kinect
            sensor.Start();

            //gesture setup-----------------------------------------------------------------------------------
            recognitionEngine = new GestureRecognitionEngine();
            recognitionEngine.AddGesture(new SwipeToLeftGesture());
            recognitionEngine.AddGesture(new SwipeToRightGesture());
            recognitionEngine.AddGesture(new ClapGesture());
            recognitionEngine.GestureRecognized += new EventHandler<GestureEventArgs>(recognitionEngine_GestureRecognized);

            //audio source--------------------------------------------------------------------------------
            sensor.AudioSource.SoundSourceAngleChanged += new EventHandler<SoundSourceAngleChangedEventArgs>(AudioSource_SoundSourceAngleChanged);
            sensor.AudioSource.BeamAngleChanged += new EventHandler<BeamAngleChangedEventArgs>(AudioSource_BeamAngleChanged);

            kinectRecognizerInfo = findKinectRecognizerInfo();
            if (kinectRecognizerInfo != null)
                recognizer =
                    new SpeechRecognitionEngine(kinectRecognizerInfo);

            buildCommands();

            // selects the beam angle using custom-written software 
            // This gives the best results
            sensor.AudioSource.BeamAngleMode = BeamAngleMode.Adaptive;

            System.IO.Stream audioStream = sensor.AudioSource.Start();

            recognizer.SetInputToAudioStream(audioStream,
                new SpeechAudioFormatInfo(EncodingFormat.Pcm,
                    16000, 16, 1, 32000, 2, null));

            recognizer.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);


            // recognize words repeatedly and asynchronously
            recognizer.RecognizeAsync(RecognizeMode.Multiple);
            
            // clean up previously stored photos
            System.IO.DirectoryInfo di = new DirectoryInfo("photos");
            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                dir.Delete(true);
            }

            Timer = new DispatcherTimer();
            Timer.Interval = new TimeSpan(0, 0, 1);
            Timer.Tick += Timer_Tick;
            //Timer.Start();

        }

        private void sensor_ColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (colorFrame == null) return;

                // get the data 
                colorFrame.CopyPixelDataTo(colorData);
                // write color data to bitmap buffer
                colorImageBitmap.WritePixels(
                    new Int32Rect(0, 0, colorFrame.Width, colorFrame.Height),
                    colorData, colorFrame.Width * colorFrame.BytesPerPixel, 0);
            }
        }

        private void sensor_SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (DrawingContext dc = this.drawingGroup.Open()) // clear the drawing
            {
                // draw a transparent background to set the render size
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0.0, 0.0, 640, 480));
                
                using (SkeletonFrame frame = e.OpenSkeletonFrame())
                {
                    if (frame != null)
                    {
                        frame.CopySkeletonDataTo(skeletons);

                        // Find the closest skeleton 
                        Skeleton skeleton = GetPrimarySkeleton(skeletons);

                        if (skeleton == null) return;
                        PoseMatching(skeleton, dc);
                        //isSuitUp = false;
                        //isJSuitUp = false;
                        recognitionEngine.StartRecognize(skeleton);
                    }
                }
            }
        }

        private Skeleton GetPrimarySkeleton(Skeleton[] skeletons)
        {
            Skeleton skeleton = null;

            if (skeletons != null)
            {
                //Find the closest skeleton       
                for (int i = 0; i < skeletons.Length; i++)
                {
                    if (skeletons[i].TrackingState == SkeletonTrackingState.Tracked)
                    {
                        if (skeleton == null) skeleton = skeletons[i];
                        else if (skeleton.Position.Z > skeletons[i].Position.Z)
                            skeleton = skeletons[i];
                    }
                }
            }

            return skeleton;
        }


        private Point SkeletonPointToScreenPoint(SkeletonPoint sp)
        {
            ColorImagePoint pt = sensor.CoordinateMapper.MapSkeletonPointToColorPoint(
                sp, ColorImageFormat.RgbResolution640x480Fps30);
            return new Point(pt.X, pt.Y);
        }

        private float GetAngle(Skeleton s, JointType js, JointType je)
        {
            Point sp = SkeletonPointToScreenPoint(s.Joints[js].Position);
            Point ep = SkeletonPointToScreenPoint(s.Joints[je].Position);

            float angle = (float)(
                Math.Atan2(ep.Y - sp.Y, ep.X - sp.X) * 180 / Math.PI);

            angle = (angle + 360) % 360; //[0.360]
            return angle;
        }

        private float AngularDifference(float a1, float a2)
        {
            float abs_diff = Math.Abs(a1 - a2);
            return Math.Min(abs_diff, 360 - abs_diff);
        }

        private Pose suitUpPose = new Pose();
        private Pose flyingPose = new Pose();
        private Pose restartPose = new Pose();


        private void PopulatePoseLibrary() // initialized in Window_Loaded
        {
            // initialize the suitUpPose below 
            //raise right hand up and put left hand down to restart
            restartPose.Title = "restart";
            PoseAngle[] angles3 = new PoseAngle[4];
            angles3[0] = new PoseAngle(JointType.ElbowRight,
                         JointType.WristRight, 270, 10);
            angles3[1] = new PoseAngle(JointType.ShoulderRight,
                         JointType.ElbowRight, 270, 10);
            angles3[2] = new PoseAngle(JointType.ShoulderLeft,
                         JointType.ElbowLeft, 90, 10);
            angles3[3] = new PoseAngle(JointType.ElbowLeft,
                         JointType.WristLeft, 90, 10);
            restartPose.Angles = angles3;

            //flying pose
            flyingPose.Title = "FlyTest";
            PoseAngle[] angles2 = new PoseAngle[4];
            angles2[0] = new PoseAngle(JointType.ShoulderRight,
                         JointType.WristRight, 75, 20);
            angles2[1] = new PoseAngle(JointType.ShoulderLeft,
                         JointType.WristLeft, 105, 20);
            angles2[2] = new PoseAngle(JointType.WristRight,
                         JointType.HandRight, 15, 30);
            angles2[3] = new PoseAngle(JointType.WristLeft,
                         JointType.HandLeft, 165, 30);
            flyingPose.Angles = angles2;

            //open arms to suitup
            suitUpPose.Title = "SuitUp";
            PoseAngle[] angles = new PoseAngle[4];
            angles[0] = new PoseAngle(JointType.ElbowRight,
                         JointType.WristRight, 0, 20);
            angles[1] = new PoseAngle(JointType.ShoulderRight,
                         JointType.ElbowRight, 0, 20);
            angles[2] = new PoseAngle(JointType.ShoulderLeft,
                         JointType.ElbowLeft, 180, 20);
            angles[3] = new PoseAngle(JointType.ElbowLeft,
                         JointType.WristLeft, 180, 20);
            suitUpPose.Angles = angles;
        }

        bool isSuitUp = false;
        bool isJSuitUp = false;


        private void suitUp(bool tf,Skeleton skeleton, DrawingContext dc)
        {
            if (tf)
            {
                DrawTorso(skeleton, dc);
                DrawHead(skeleton, dc);

                DrawRightThigh(skeleton, dc);
                DrawRightCalf(skeleton, dc);
                DrawLeftThigh(skeleton, dc);
                DrawLeftCalf(skeleton, dc);

                DrawRightUpperHand(skeleton, dc);
                DrawLeftUpperHand(skeleton, dc);
                DrawRightLowerHand(skeleton, dc);
                DrawLeftLowerHand(skeleton, dc);


                if (!suitUpPlaying)
                {

                    suitUpSound.Play();
                    suitUpPlaying = true;
                }

                hud.Visibility = System.Windows.Visibility.Visible;

                blackImg.Visibility = System.Windows.Visibility.Visible;
                colorImg.Visibility = System.Windows.Visibility.Hidden;
            }
            
        }
        private void flyTest(Skeleton skeleton, DrawingContext dc)
        {
            DrawBeam(skeleton, dc, JointType.ElbowRight, JointType.WristRight, JointType.HandRight);
            DrawBeam(skeleton, dc, JointType.ElbowLeft, JointType.WristLeft, JointType.HandLeft);
            if (!flyPlaying && suitUpPlaying)
            {
                flySound.Play();
                flyPlaying = true;  
            }
        }

        private void PoseMatching(Skeleton skeleton, DrawingContext dc)
        {
            isMatched(skeleton, suitUpPose);
            //isMatched(skeleton, restartPose);
            if (isSuitUp && isJSuitUp)
            {   
                // suit up trigger------------------------------------------------------------      
                suitUp(isSuitUp, skeleton, dc);

                if (isMatchedWhile(skeleton, flyingPose))
                {
                    Debug.WriteLine("is matched while");
                    flyTest(skeleton, dc);
                }
                
            }    
        }

        void isMatched(Skeleton skeleton, Pose pose)
        {
            int count = 0;
            for (int i = 0; i < suitUpPose.Angles.Length; i++)
            {
                if (AngularDifference(suitUpPose.Angles[i].Angle,
                        GetAngle(skeleton, suitUpPose.Angles[i].StartJoint, suitUpPose.Angles[i].EndJoint)
                    ) < suitUpPose.Angles[i].Threshold)
                {
                    count++;
                    //isSuitUp = true;
                }
            }
            if (count == suitUpPose.Angles.Length)
            {
                isSuitUp = true;
                count = 0;
            }
        }

        Boolean isMatchedWhile(Skeleton skeleton, Pose pose) 
        {
            //Math.Abs(current-target)<threshold
            int count = 0;
            for (int i = 0; i < flyingPose.Angles.Length; i++)
            {
                if (AngularDifference(flyingPose.Angles[i].Angle,
                        GetAngle(skeleton, flyingPose.Angles[i].StartJoint, flyingPose.Angles[i].EndJoint)
                    ) < flyingPose.Angles[i].Threshold)
                {
                    count++;
                }
            }
            if (count == flyingPose.Angles.Length)
            {
                return true;
            }
            else
            {
                count = 0;
                return false;
                
            }
        }
        
        /// <summary>
        /// //////////////////////////////////
        /// </summary>
        private void LoadImages() // called in Window_Loaded
        {
            torso = new BitmapImage(
                new Uri("Images/torso.png", UriKind.Relative));

            helmet = new BitmapImage(
                new Uri("Images/head.png", UriKind.Relative));

            bug = new BitmapImage(
                new Uri("Images/bug.png", UriKind.Relative));

            rightUpperArm = new BitmapImage(
                new Uri("Images/up_right_arm.png", UriKind.Relative));

            rightThigh = new BitmapImage(
                new Uri("Images/right_thigh.png", UriKind.Relative));

            rightCalf = new BitmapImage(
                new Uri("Images/right_calf.png", UriKind.Relative));

            leftThigh = new BitmapImage(
                new Uri("Images/left_thigh.png", UriKind.Relative));

            leftCalf = new BitmapImage(
                new Uri("Images/left_calf.png", UriKind.Relative));

        }


        private Rect r = new Rect(0, 0, 200, 200);
        private void DrawTorso(Skeleton skeleton,
            DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            Point sLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ShoulderLeft].Position);
            Point sCenter = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ShoulderCenter].Position);
            Point sRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ShoulderRight].Position);
            Point hCenter = SkeletonPointToScreenPoint(skeleton.Joints[JointType.HipCenter].Position);

            r.Width = Math.Abs(sRight.X - sLeft.X) * 1.3;
            r.Height = Math.Abs(hCenter.Y - sCenter.Y) * 2;

            r.X = sCenter.X - r.Width / 2;
            r.Y = sCenter.Y - r.Height / 18;
            
                dc.DrawImage(torso, r);
        }

        private Rect r_head = new Rect(0, 0, 200, 200);
        private void DrawHead(Skeleton skeleton,DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            Point head = SkeletonPointToScreenPoint(skeleton.Joints[JointType.Head].Position);
            Point sCenter = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ShoulderCenter].Position);
            r_head.Width =  Math.Abs(sCenter.Y - head.Y) * 1.2;
            r_head.Height = Math.Abs(sCenter.Y - head.Y) * 1.5;
            r_head.X = sCenter.X - (r_head.Width/2);
            r_head.Y = sCenter.Y -r_head.Height;

                dc.DrawImage(helmet, r_head);
        }

        private Rect r_right_thigh = new Rect(0, 0, 200, 200);
        private void DrawRightThigh(Skeleton skeleton, DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            Point hipRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.HipRight].Position);
            Point kneeRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.KneeRight].Position);
            Point hipCenter = SkeletonPointToScreenPoint(skeleton.Joints[JointType.HipCenter].Position);
            r_right_thigh.Width = Math.Abs(hipRight.Y - hipCenter.Y) * 2.5;
            r_right_thigh.Height = Math.Abs(hipRight.Y - kneeRight.Y) * 1.5;
            r_right_thigh.X = hipRight.X - Math.Abs(hipRight.Y - hipCenter.Y)*0.35 ;
            r_right_thigh.Y = hipRight.Y + 50;

            dc.DrawImage(rightThigh, r_right_thigh);
        }

        private Rect r_left_thigh = new Rect(0, 0, 200, 200);
        private void DrawLeftThigh(Skeleton skeleton, DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            Point hipLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.HipLeft].Position);
            Point kneeLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.KneeLeft].Position);
            Point hipCenter = SkeletonPointToScreenPoint(skeleton.Joints[JointType.HipCenter].Position);
            Point hipRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.HipRight].Position);
            r_left_thigh.Width = Math.Abs(hipCenter.Y - hipLeft.Y) * 2.5;
            r_left_thigh.Height = Math.Abs(hipLeft.Y - kneeLeft.Y) * 1.5;
            r_left_thigh.X = hipLeft.X + Math.Abs(hipRight.Y - hipCenter.Y) * 0.35 - r_left_thigh.Width;
            r_left_thigh.Y = hipLeft.Y + 50;

            dc.DrawImage(leftThigh, r_left_thigh);
        }

        private Rect r_right_calf = new Rect(0, 0, 200, 200);
        private void DrawRightCalf(Skeleton skeleton, DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            Point kneeRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.KneeRight].Position);
            Point ankleRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.AnkleRight].Position);
            Point hipCenter = SkeletonPointToScreenPoint(skeleton.Joints[JointType.HipCenter].Position);
            Point hipRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.HipRight].Position);
            r_right_calf.Width = Math.Abs(hipRight.Y - hipCenter.Y) * 0.5;
            r_right_calf.Height = Math.Abs(kneeRight.Y - ankleRight.Y) * 1.5;
            r_right_calf.X = ankleRight.X - 20;
            r_right_calf.Y = ankleRight.Y + 50;

            dc.DrawImage(rightCalf, r_right_calf);
        }

        private Rect r_left_calf = new Rect(0, 0, 200, 200);
        private void DrawLeftCalf(Skeleton skeleton, DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            Point kneeLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.KneeLeft].Position);
            Point ankleLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.AnkleLeft].Position);
            r_left_calf.Width = Math.Abs(kneeLeft.Y - ankleLeft.Y) * 0.5;
            r_left_calf.Height = Math.Abs(kneeLeft.Y - ankleLeft.Y) * 1.5;
            r_left_calf.X = ankleLeft.X - 20;
            r_left_calf.Y = ankleLeft.Y + 50;

            dc.DrawImage(leftCalf, r_left_calf);
        }

        private Rect r_upRightHand = new Rect(0, 0, 200, 200);
        private void DrawRightUpperHand(Skeleton skeleton,DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            float angle;
            Point shoulderRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ShoulderRight].Position);
            Point elbowRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ElbowRight].Position);

            upRightArm.Visibility = System.Windows.Visibility.Visible;
            angle = GetAngle(skeleton, JointType.ShoulderRight, JointType.ElbowRight);
            RotateTransform rotateTransform = new RotateTransform(angle-90);
            upRightArm.RenderTransform = rotateTransform;

            upRightArm.Height = Math.Sqrt((shoulderRight.Y - elbowRight.Y) * (shoulderRight.Y - elbowRight.Y) + (shoulderRight.X - elbowRight.X) * (shoulderRight.X - elbowRight.X)) * 1.3;
            upRightArm.Width = Math.Sqrt((shoulderRight.Y - elbowRight.Y) * (shoulderRight.Y - elbowRight.Y) + (shoulderRight.X - elbowRight.X) * (shoulderRight.X - elbowRight.X)) * 0.85;

            upRightArm.Margin = new Thickness(shoulderRight.X - upRightArm.Height * 0.2, shoulderRight.Y - upRightArm.Height * 0.25, 0, 0);
            //Point sRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ShoulderRight].Position);
            //Point eRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ElbowRight].Position);
            //double width =  Math.Sqrt((sRight.Y-eRight.Y)*(sRight.Y-eRight.Y) + (sRight.X-eRight.X)*(sRight.X-eRight.X))*0.5;
            //dc.DrawLine(new Pen(Brushes.Red, width), sRight, eRight);
            
            //dc.DrawImage(rightUpperArm, r_upRightHand);
        }

        private Rect lowRightHand = new Rect(0, 0, 200, 200);
        private void DrawRightLowerHand(Skeleton skeleton, DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            float angle;
            Point wristRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.WristRight].Position);
            Point elbowRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ElbowRight].Position);

            lowRightArm.Visibility = System.Windows.Visibility.Visible;
            angle = GetAngle(skeleton, JointType.ElbowRight, JointType.WristRight);
            RotateTransform rotateTransform = new RotateTransform(angle-75);
            lowRightArm.RenderTransform = rotateTransform;

            lowRightArm.Height = Math.Sqrt((elbowRight.Y - wristRight.Y) * (elbowRight.Y - wristRight.Y) + (elbowRight.X - wristRight.X) * (elbowRight.X - wristRight.X)) * 2.2;
            lowRightArm.Width = Math.Sqrt((elbowRight.Y - wristRight.Y) * (elbowRight.Y - wristRight.Y) + (elbowRight.X - wristRight.X) * (elbowRight.X - wristRight.X)) * 0.65;

            lowRightArm.Margin = new Thickness(elbowRight.X - lowRightArm.Height * 0.05, elbowRight.Y - lowRightArm.Height * 0.1, 0, 0);
            //Point sRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ShoulderRight].Position);
            //Point eRight = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ElbowRight].Position);
            //double width =  Math.Sqrt((sRight.Y-eRight.Y)*(sRight.Y-eRight.Y) + (sRight.X-eRight.X)*(sRight.X-eRight.X))*0.5;
            //dc.DrawLine(new Pen(Brushes.Red, width), sRight, eRight);

            //dc.DrawImage(rightUpperArm, r_upRightHand);
        }

        private void DrawLeftUpperHand(Skeleton skeleton, DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            float angle;
            Point shoulderLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ShoulderLeft].Position);
            Point elbowLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ElbowLeft].Position);

            upLeftArm.Visibility = System.Windows.Visibility.Visible;

            angle = GetAngle(skeleton, JointType.ShoulderLeft, JointType.ElbowLeft);
            RotateTransform rotateTransform = new RotateTransform(angle - 90);
            upLeftArm.RenderTransform = rotateTransform;

            upLeftArm.Height = Math.Sqrt((shoulderLeft.Y - elbowLeft.Y) * (shoulderLeft.Y - elbowLeft.Y) + (shoulderLeft.X - elbowLeft.X) * (shoulderLeft.X - elbowLeft.X)) * 1.3;
            upLeftArm.Width = Math.Sqrt((shoulderLeft.Y - elbowLeft.Y) * (shoulderLeft.Y - elbowLeft.Y) + (shoulderLeft.X - elbowLeft.X) * (shoulderLeft.X - elbowLeft.X)) * 0.8;

            upLeftArm.Margin = new Thickness(shoulderLeft.X - upLeftArm.Height * 0.4, shoulderLeft.Y - upLeftArm.Height * 0.25, 0, 0);
        }

        private void DrawLeftLowerHand(Skeleton skeleton, DrawingContext dc) // called in sensor_SkeletonFrameReady
        {
            float angle;
            Point wristLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.WristLeft].Position);
            Point elbowLeft = SkeletonPointToScreenPoint(skeleton.Joints[JointType.ElbowLeft].Position);

            lowLeftArm.Visibility = System.Windows.Visibility.Visible;
            angle = GetAngle(skeleton, JointType.ElbowLeft, JointType.WristLeft);
            RotateTransform rotateTransform = new RotateTransform(angle - 105);
            lowLeftArm.RenderTransform = rotateTransform;

            lowLeftArm.Height = Math.Sqrt((elbowLeft.Y - wristLeft.Y) * (elbowLeft.Y - wristLeft.Y) + (elbowLeft.X - wristLeft.X) * (elbowLeft.X - wristLeft.X)) * 2.2;
            lowLeftArm.Width = Math.Sqrt((elbowLeft.Y - wristLeft.Y) * (elbowLeft.Y - wristLeft.Y) + (elbowLeft.X - wristLeft.X) * (elbowLeft.X - wristLeft.X)) * 0.65;

            lowLeftArm.Margin = new Thickness(elbowLeft.X - lowLeftArm.Height * 0.2, elbowLeft.Y - lowLeftArm.Height * 0.1, 0, 0);

        }

        SolidColorBrush beam = new SolidColorBrush(Color.FromArgb(128, 68, 240, 255));
        private Rect r_fire = new Rect(0, 0, 200, 200);
        private void DrawBeam(Skeleton skeleton, DrawingContext dc,
            JointType j1, JointType j2, JointType j3)
        {
            
            Point pt1 = SkeletonPointToScreenPoint(skeleton.Joints[j1].Position);
            Point pt2 = SkeletonPointToScreenPoint(skeleton.Joints[j2].Position);
            Point pt3 = SkeletonPointToScreenPoint(skeleton.Joints[j3].Position);
            Vector dir = new Vector(pt2.X - pt1.X, pt2.Y - pt1.Y);

            pt2 = pt1 + dir * 5;
            dc.DrawLine(new Pen(beam, 30), pt3, pt2);
            //dc.DrawEllipse(Brushes.Red, null, pt3, 10, 10);
            //dc.DrawImage(fire, r_fire);

            dir.Normalize();//unit vector |dir| = 1; magnitude:1 pixel

            double head_size = 50;
            pt1 = pt2;
            pt2 = pt1 + dir * head_size;
            //dc.DrawLine(new Pen(Brushes.Black, head_size), pt1, pt2);

            //hit test
            Point pt_head_center = pt1 + dir * (head_size / 2);
            // Point bug_center = new Point(r_bug.X + r_bug.Width / 2, r_bug.Y + r_bug.Height / 2);

            //Vector v = new Vector(bug_center.X - pt_head_center.X, bug_center.Y - pt_head_center.Y);
            //if (v.Length < head_size / 2)//hit
            //if(r_bug.Contains(pt_head_center))
            //{
            //    Random rand = new Random();
            //    r_bug.Y = -r_bug.Height;
            //    r_bug.X = rand.Next((int)skeletonImg.Width);
            //}

        }
        private void restart()
        {
            hud.Visibility = System.Windows.Visibility.Hidden;
            hudRed.Visibility = System.Windows.Visibility.Hidden;
            colorImg.Visibility = System.Windows.Visibility.Visible;
            blackImg.Visibility = System.Windows.Visibility.Hidden;

            upRightArm.Visibility = System.Windows.Visibility.Hidden;
            upLeftArm.Visibility = System.Windows.Visibility.Hidden;
            lowRightArm.Visibility = System.Windows.Visibility.Hidden;
            lowLeftArm.Visibility = System.Windows.Visibility.Hidden;

            isSuitUp = false;
            isJSuitUp = false;
        
            suitUpPlaying = false;
            flyPlaying = false;
            servicePlaying = false;
        }


        private void recognizer_SpeechRecognized(
object sender, SpeechRecognizedEventArgs e)
        {
            recognizedColorTxt.Text = "Recognized Color: " + e.Result.Text + "; confidence: " + e.Result.Confidence;

            if (e.Result.Confidence < 0.1) return;

            if (e.Result.Text.ToLower().Contains("reset"))
            {

            }
            else if (e.Result.Text.ToLower().Contains("hey jarvis"))
            {
                if (e.Result.Text.ToLower().Contains("take a photo"))
                {
                    Debug.WriteLine("taking photo");
                    photoNo++;
                    time = 4;
                    TBCountDown.Visibility = System.Windows.Visibility.Visible;
                    Timer.Start();
                    //takePhoto();
                }
                else if (e.Result.Text.ToLower().Contains("suit me"))
                {
                    if (e.Result.Text.ToLower().Contains("up"))
                    {
                        isJSuitUp = true;
                    }
                    else if (e.Result.Text.ToLower().Contains("down"))
                    {
                        restart();
                    }
                }
            }
        }

        private void buildCommands()
        {
            GrammarBuilder grammarBuilder = new GrammarBuilder("hey jarvis");

            // the same culture as the recognizer (US English)
            grammarBuilder.Culture = kinectRecognizerInfo.Culture;
            //grammarBuilder.Append(commands);
            grammarBuilder.Append("suit me");
            grammarBuilder.Append(new Choices("up", "down"));

            Grammar grammar = new Grammar(grammarBuilder);

            GrammarBuilder grammarBuilder2 = new GrammarBuilder("reset");
            grammarBuilder2.Culture = kinectRecognizerInfo.Culture;
            //grammarBuilder2.Append(commands);
            Grammar grammar2 = new Grammar(grammarBuilder2);

            GrammarBuilder grammarBuilder3 = new GrammarBuilder("hey jarvis");
            grammarBuilder3.Culture = kinectRecognizerInfo.Culture;
            grammarBuilder3.Append("take a photo");
            Grammar grammar3 = new Grammar(grammarBuilder3);

            recognizer.LoadGrammar(grammar);
            recognizer.LoadGrammar(grammar2);
            recognizer.LoadGrammar(grammar3);
        }

        private RecognizerInfo findKinectRecognizerInfo()
        {
            var recognizers =
                SpeechRecognitionEngine.InstalledRecognizers();

            foreach (RecognizerInfo recInfo in recognizers)
            {
                // look at each recognizer info value 
                // to find the one that works for Kinect
                if (recInfo.AdditionalInfo.ContainsKey("Kinect"))
                {
                    string details = recInfo.AdditionalInfo["Kinect"];
                    if (details == "True"
            && recInfo.Culture.Name == "en-US")
                    {
                        // If we get here we have found 
                        // the info we want to use
                        return recInfo;
                    }
                }
            }
            return null;
        }


        void AudioSource_BeamAngleChanged(object sender, BeamAngleChangedEventArgs e)
        {
            beamAngleTxt.Text = "Beam angle:" + e.Angle;
            //soundSourceAngleTxt.Text = "Sound source angle:" + e.Angle;
        }
        void AudioSource_SoundSourceAngleChanged(object sender, SoundSourceAngleChangedEventArgs e)
        {
            //throw new NotImplementedException();
            soundSourceAngleTxt.Text = "Sound source angle:" + e.Angle;
        }

        void recognitionEngine_GestureRecognized(object sender, GestureEventArgs e)
        {
            switch (e.GestureType)
            {
                //trigger actions for gesture
                case GestureType.SwipeToLeft:
                    //recognitionResults.Items.Add(e.GestureType.ToString());
                    if(isSuitUp)
                    {
                        if (hudRed.Visibility == System.Windows.Visibility.Hidden)
                        {
                            hudRed.Visibility = System.Windows.Visibility.Visible;
                        }
                        else if (hudRed.Visibility == System.Windows.Visibility.Visible)
                        {
                            hudRed.Visibility = System.Windows.Visibility.Hidden;
                        }

                    }
                    Debug.WriteLine("swipe to left");
                    break;
                case GestureType.SwipeToRight:
                    //recognitionResults.Items.Clear();
                    if (isSuitUp)
                    {
                        if (hudRed.Visibility == System.Windows.Visibility.Hidden)
                        {
                            hudRed.Visibility = System.Windows.Visibility.Visible;
                        }
                        else if (hudRed.Visibility == System.Windows.Visibility.Visible)
                        {
                            hudRed.Visibility = System.Windows.Visibility.Hidden;
                        }

                    }
                    Debug.WriteLine("swipe to right");
                    break;
                case GestureType.Clap:
                    if (!servicePlaying)
                    {
                        serviceSound.Play();
                        servicePlaying = true;
                        hud.Visibility = System.Windows.Visibility.Visible;
                    }
                    break;
            }

        }

        // output window as png
        public int photoNo = 0;
        public void takePhoto()
        {
            RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32);
            renderTargetBitmap.Render(window);
            PngBitmapEncoder pngImage = new PngBitmapEncoder();
            pngImage.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
            using (Stream fileStream = File.Create("photos/photo"+photoNo+".png"))
            {
                pngImage.Save(fileStream);
                Debug.WriteLine("image saved");
            }
        }

        private int time = 4;
        private DispatcherTimer Timer;

        void Timer_Tick(object sender, EventArgs e)
        {
            if (time > 0)
            {
                time--;
                TBCountDown.Text = string.Format("{1}", time / 60, time % 60);
            }
            else
            {
                time = 4;
                Timer.Stop();
                TBCountDown.Visibility = System.Windows.Visibility.Hidden;
                takePhoto();
                Process.Start(@"photos");
            }
        }

    }
}