using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using OpenCvSharp;
using RealTimeFaceApi.Core;
using RealTimeFaceApi.Core.Data;
using RealTimeFaceApi.Core.Filters;
using RealTimeFaceApi.Core.Trackers;
using RecognitionInfoPanel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace RealTimeFaceApi.Cmd
{
    public static class Program
    {
        // TODO: Add Face API subscription key.
        private static string FaceSubscriptionKey = "d44d11ecdc0141b19c3ea6d694263dcb";

        // TODO: Add face group ID.
        private static string FaceGroupId = "nbgaccn";


        private static readonly Scalar _faceColorBrush = new Scalar(0, 0, 255);
        private static FaceClient _faceClient;
        private static Task _faceRecognitionTask = null;
        private static IList<Microsoft.Azure.CognitiveServices.Vision.Face.Models.Person> _cachedIdentities = null;

        public static void Main(string[] args)
        {
            _faceClient = new FaceClient(new ApiKeyServiceClientCredentials(FaceSubscriptionKey))
            {
                Endpoint = "https://zackthemanoamanaman.cognitiveservices.azure.com/"
            };

            string filename = args.FirstOrDefault();
            Run(filename);
        }

        private static void Run(string filename)
        {
            string personGroupId = "nbgaccn";
            _faceClient.PersonGroup.CreateAsync(personGroupId, "NBG Accenture");

            Task<Person> ZachariasSiatris = _faceClient.PersonGroupPerson.CreateAsync(personGroupId, "Zacharias Siatris");

            List<ZSPerson> ZSPersonsList = new List<ZSPerson>();

            //Zacharias Siatris
            ZSPerson zachariasSiatris = new ZSPerson();
            zachariasSiatris.name = "Zacharias Siatris";
            zachariasSiatris.company = " Accenture Inc.";
            zachariasSiatris.description = " ROTATING TO THE NEW!!";
            zachariasSiatris.id = " 1123456979";
            zachariasSiatris.isDangerous = true;
            zachariasSiatris.projectTeam = " AI - Innovation Team";
            zachariasSiatris.nbgSection = " In the \"NEW\" Management";
            zachariasSiatris.photoPathId = @"C:\Users\zacharias.siatris\Desktop\FaceAppPhotos\ZachariasSiatris\ZackID.jpg";
            ZSPersonsList.Add(zachariasSiatris);

            const string zachariasSiatrisImageDir = @"C:\Users\zacharias.siatris\Desktop\FaceAppPhotos\ZachariasSiatris";
           

            foreach (string imagePath in Directory.GetFiles(zachariasSiatrisImageDir, "*.jpg"))
            {
                using (Stream s = File.OpenRead(imagePath))
                {
                    _faceClient.PersonGroupPerson.AddFaceFromStreamAsync(
                         personGroupId, ZachariasSiatris.Result.PersonId, s);
                }
            }

            _faceClient.PersonGroup.TrainAsync(personGroupId);

            TrainingStatus trainingStatus = null;
            while (true)
            {
                trainingStatus = _faceClient.PersonGroup.GetTrainingStatusAsync(personGroupId).Result;

                if (trainingStatus.Status != TrainingStatusType.Running)
                {
                    break;
                }

                Task.Delay(1000);
            }

            int timePerFrame;
            VideoCapture capture;
            if (!string.IsNullOrWhiteSpace(filename) && File.Exists(filename))
            {
                // If filename exists, use that as a source of video.
                capture = InitializeVideoCapture(filename);

                // Allow just enough time to paint the frame on the window.
                timePerFrame = 1;
            }
            else
            {
                // Otherwise use the webcam.
                capture = InitializeCapture(0);

                // Time required to wait until next frame.
                timePerFrame = (int)Math.Round(1000 / capture.Fps);
            }

            // Input was not initialized.
            if (capture == null)
            {
                Console.ReadKey();
                return;
            }

            // Initialize face detection algorithm.
            CascadeClassifier haarCascade = InitializeFaceClassifier();

            // List of simple face filtering algorithms.
            var filtering = new SimpleFaceFiltering(new IFaceFilter[]
            {
                new TooSmallFacesFilter(20, 20)
            });

            // List of simple face tracking algorithms.
            var trackingChanges = new SimpleFaceTracking(new IFaceTrackingChanged[]
            {
                new TrackNumberOfFaces(),
                new TrackDistanceOfFaces { Threshold = 2000 }
            });

            InfoPanel panel = new InfoPanel();
            panel.Show();

            // Open a new window via OpenCV.
            using (Window window = new Window("capture"))
            {
                using (Mat image = new Mat())
                {
                    while (true)
                    {
                        // Get current frame.
                        capture.Read(image);
                        if (image.Empty())
                            continue;

                        // Detect faces
                        var faces = DetectFaces(haarCascade, image);

                        // Filter faces
                        var state = faces.ToImageState();
                        state = filtering.FilterFaces(state);

                        // Determine change
                        var hasChange = trackingChanges.ShouldUpdateRecognition(state);

                        if (hasChange)
                        {
                            Console.WriteLine("Changes detected...");
                            Random random = new Random();
                            Font currentFont = panel.richTextBox1.SelectionFont;
                            FontStyle newFontStyle = (FontStyle)(currentFont.Style | FontStyle.Bold);
                            panel.richTextBox1.SelectionFont = new Font(currentFont.FontFamily, 12, newFontStyle);
                            panel.richTextBox1.AppendText("\n- Changes detected...");

                            for (int i = 0; i < 20; i++)
                                panel.richTextBox5.AppendText(random.Next(0, 2).ToString());

                            // Identify faces if changed and previous identification finished.
                            if (_faceRecognitionTask == null && !string.IsNullOrWhiteSpace(FaceSubscriptionKey))
                                _faceRecognitionTask = StartRecognizing(image, panel, ZSPersonsList);
                        }
                        using (var renderedFaces = RenderFaces(state, image))
                        {
                            // Update popup window.
                            window.ShowImage(renderedFaces);
                        }

                        // Wait for next frame and allow Window to be repainted.
                        Cv2.WaitKey(timePerFrame);
                    }
                }
            }
        }

        private static void AppendText(this RichTextBox box, string text, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = color;
            box.AppendText(text);
            box.SelectionColor = box.ForeColor;
        }


        /// <summary>
        /// Use Microsoft Cognitive Services to recognize their faces.
        /// </summary>
        /// <param name="image">Video or web cam frame.</param>
        private static async Task StartRecognizing(Mat image, InfoPanel panel, List<ZSPerson> ZSPersonsList)
        {
            try
            {
                //Console.WriteLine(DateTime.Now + ": Attempting to recognize faces...");
                panel.richTextBox1.AppendText("\nAttempting to recognize faces...");

                var stream = image.ToMemoryStream();
                var detectedFaces = await _faceClient.Face.DetectWithStreamAsync(stream, true, true);
                var faceIds = detectedFaces.Where(f => f.FaceId.HasValue).Select(f => f.FaceId.Value).ToList();

                if (faceIds.Any())
                {
                    var potentialUsers = await _faceClient.Face.IdentifyAsync(faceIds, FaceGroupId);
                    foreach (var candidate in potentialUsers.Select(u => u.Candidates.FirstOrDefault()))
                    {
                        var candidateName = await GetCandidateName(candidate?.PersonId);
                        panel.pictureBox2.Image = new Bitmap(@"C:\Users\zacharias.siatris\Desktop\FaceAppPhotos\accentureAI.jpg");
                        //Console.WriteLine($"{DateTime.Now}: {candidateName}");
                        if (candidateName.Trim() == "No Person ID")
                        {
                            panel.richTextBox4.Clear();
                            panel.richTextBox4.AppendText("DANGER!!\n");
                            panel.richTextBox4.AppendText("This person is UNKNOWN !!\n");
                            panel.BackColor = Color.DarkRed;
                            panel.richTextBox4.BackColor = Color.DarkRed;
                            panel.richTextBox4.ForeColor = Color.White;
                            panel.richTextBox3.Clear();
                            panel.pictureBox1.Image = new Bitmap(@"C:\Users\zacharias.siatris\Desktop\FaceAppPhotos\unknown.jpg");
                        }
                        else
                        {
                            panel.pictureBox1.Image = new Bitmap(ZSPersonsList.Find(x => x.name == candidateName).photoPathId);
                            if (ZSPersonsList.Find(x => x.name == candidateName).isDangerous)
                            {
                                panel.richTextBox4.Clear();
                                panel.richTextBox4.AppendText("WARNING!!\n");
                                panel.richTextBox4.AppendText("This person owes money to the bank.\n\n");
                                panel.richTextBox4.AppendText(ZSPersonsList.Find(x => x.name == candidateName).name + "\n", Color.White);
                                panel.richTextBox4.AppendText(ZSPersonsList.Find(x => x.name == candidateName).projectTeam, Color.White);
                                panel.BackColor = Color.DarkRed;
                                panel.richTextBox4.BackColor = Color.DarkRed;
                                panel.richTextBox4.ForeColor = Color.White;
                            }
                            else
                            {
                                panel.richTextBox4.Clear();
                                panel.richTextBox4.AppendText("VALID CHECK!!\n\n");
                                panel.richTextBox4.AppendText(ZSPersonsList.Find(x => x.name == candidateName).name + "\n", Color.White);
                                panel.richTextBox4.AppendText(ZSPersonsList.Find(x => x.name == candidateName).projectTeam, Color.White);
                                //panel.richTextBox4.AppendText("Pretty personality full of confidence.\n");
                                panel.BackColor = Color.Green;
                                panel.richTextBox4.BackColor = Color.Green;
                                panel.richTextBox4.ForeColor = Color.White;
                            }

                            panel.richTextBox3.Clear();

                            panel.richTextBox3.AppendText("COMPANY:\n", Color.YellowGreen);
                            panel.richTextBox3.AppendText(ZSPersonsList.Find(x => x.name == candidateName).company + "\n\n", Color.White);

                            panel.richTextBox3.AppendText("ENTRANCE TIME:\n", Color.YellowGreen);
                            panel.richTextBox3.AppendText(DateTime.Now + "\n\n", Color.White);

                            panel.richTextBox3.AppendText("PROJECT DESCRIPTION:\n", Color.YellowGreen);
                            panel.richTextBox3.AppendText(ZSPersonsList.Find(x => x.name == candidateName).description + "\n\n", Color.White);

                            panel.richTextBox3.AppendText("ID:\n", Color.YellowGreen);
                            panel.richTextBox3.AppendText(ZSPersonsList.Find(x => x.name == candidateName).id + "\n\n", Color.White);

                            panel.richTextBox3.AppendText("SECTION:\n", Color.YellowGreen);
                            panel.richTextBox3.AppendText(ZSPersonsList.Find(x => x.name == candidateName).nbgSection + "\n\n", Color.White);

                            panel.pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
                            panel.pictureBox2.SizeMode = PictureBoxSizeMode.StretchImage;
                            Font currentFont = panel.richTextBox1.SelectionFont;
                            FontStyle newFontStyle = (FontStyle)(currentFont.Style | FontStyle.Bold);
                            panel.richTextBox1.SelectionFont = new Font(currentFont.FontFamily, 14, newFontStyle);
                            panel.richTextBox1.AppendText("\n" + DateTime.Today.ToString() + "   -   " + candidateName);
                        }
                        panel.Refresh();
                    }
                }
            }
            catch (APIErrorException apiError)
            {
                panel.richTextBox1.AppendText("\nCognitive service error: " + apiError?.Body?.Error?.Message);
                Console.WriteLine("Cognitive service error: " + apiError?.Body?.Error?.Message);
            }
            catch (Exception e)
            {
                Console.WriteLine("Getting identity failed: " + e.ToString());
            }

            _faceRecognitionTask = null;
        }

        private async static Task<string> GetCandidateName(Guid? personId)
        {
            if (!personId.HasValue)
            {
                return "No Person ID";
            }

            if (_cachedIdentities == null)
            {
                _cachedIdentities = await _faceClient.PersonGroupPerson.ListAsync(FaceGroupId);
            }

            return _cachedIdentities
                ?.Where(i => i.PersonId == personId)
                .Select(i => i.Name)
                .FirstOrDefault()
                ?? "Candidate not found";
        }

        /// <summary>
        /// Initialize classifier used for offline face detection.
        /// </summary>
        private static CascadeClassifier InitializeFaceClassifier()
        {
            return new CascadeClassifier("Data/haarcascade_frontalface_alt.xml");
        }

        /// <summary>
        /// Initialize web cam capture.
        /// </summary>
        /// <returns>Returns web cam capture.</returns>
        private static VideoCapture InitializeCapture(int cameraIndex = 0)
        {
            VideoCapture capture = new VideoCapture();
            capture.Open(CaptureDevice.MSMF, cameraIndex);

            if (!capture.IsOpened())
            {
                Console.WriteLine("Unable to open capture.");
                return null;
            }

            return capture;
        }

        /// <summary>
        /// Initializes video capture for video files.
        /// </summary>
        /// <param name="file">Path to a video.</param>
        /// <returns>Return video file capture.</returns>
        private static VideoCapture InitializeVideoCapture(string file)
        {
            var capture = new VideoCapture(file);
            if (!capture.IsOpened())
            {
                Console.WriteLine("Unable to open video file {0}.", file);
                return null;
            }

            return capture;
        }

        /// <summary>
        /// Use OpenCV Cascade classifier to do offline face detection.
        /// </summary>
        /// <param name="cascadeClassifier">OpenCV cascade classifier.</param>
        /// <param name="image">Web cam or video frame.</param>
        /// <returns>Return list of faces as rectangles.</returns>
        private static Rect[] DetectFaces(CascadeClassifier cascadeClassifier, Mat image)
        {
            return cascadeClassifier
                .DetectMultiScale(
                    image,
                    1.08,
                    2,
                    HaarDetectionType.ScaleImage,
                    new OpenCvSharp.Size(60, 60));
        }

        /// <summary>
        /// Render detected faces via OpenCV.
        /// </summary>
        /// <param name="state">Current frame state.</param>
        /// <param name="image">Web cam or video frame.</param>
        /// <returns>Returns new image frame.</returns>
        private static Mat RenderFaces(FrameState state, Mat image)
        {
            Mat result = image.Clone();
            Cv2.CvtColor(image, image, ColorConversionCodes.BGR2GRAY);

            // Render all detected faces
            foreach (var face in state.Faces)
            {
                var center = new OpenCvSharp.Point
                {
                    X = face.Center.X,
                    Y = face.Center.Y
                };
                var axes = new OpenCvSharp.Size
                {
                    Width = (int)(face.Size.Width * 0.5) + 10,
                    Height = (int)(face.Size.Height * 0.5) + 10
                };

                Cv2.Ellipse(result, center, axes, 0, 0, 360, _faceColorBrush, 4);
            }

            return result;
        }
    }
}
