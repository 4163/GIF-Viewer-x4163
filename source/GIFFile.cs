using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using GIF_Viewer.GifComponents;
using GIF_Viewer.GifComponents.Enums;
using GIF_Viewer.Utils;

namespace GIF_Viewer
{
    /// <summary>
    /// Represents a GIF file
    /// </summary>
    public class GifFile : IDisposable
    {
        /// <summary>
        /// Path to the GIF file
        /// </summary>
        public string GifPath = "";
        /// <summary>
        /// Image object representing the current GIF
        /// </summary>
        public Image Gif;

        /// <summary>
        /// The decoder holding the information about the currently loaded .gif file
        /// </summary>
        private GifDecoder _gifDecoded;

        /// <summary>
        /// Fully decoded frames loaded by the external Pillow helper.
        /// </summary>
        private Bitmap[] _helperFrames;

        /// <summary>
        /// Whether there is a GIF file currently loaded on this GIFFile object
        /// </summary>
        public bool Loaded { get; private set; }

        /// <summary>
        /// Whether the GIF file is playing
        /// </summary>
        public bool Playing = false;
        /// <summary>
        /// Gets the ammount of frames on this gif
        /// </summary>
        public int FrameCount { get; private set; }

        /// <summary>
        /// Gets or sets the current frame being displayed
        /// </summary>
        public int CurrentFrame
        {
            get => _currentFrame;
            set => SetCurrentFrame(value);
        }
        /// <summary>
        /// Intervals (in ms) between each frame
        /// </summary>
        public int[] Intervals;
        /// <summary>
        /// Gets the current frame interval (in milliseconds)
        /// </summary>
        public int CurrentInterval => _currentInterval;

        /// <summary>
        /// Whether the GIF file should loop
        /// </summary>
        public bool CanLoop;

        /// <summary>
        /// Gets the Width of this GIF file
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the Height of this GIF file
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Current frame interval (in milliseconds)
        /// </summary>
        private int _currentInterval;

        /// <summary>
        /// The current frame being displayed
        /// </summary>
        private int _currentFrame;

        /// <summary>
        /// Disposes of this GIF file
        /// </summary>
        public void Dispose()
        {
            if (Gif != null)
            {
                Gif.Dispose();
                Gif = null;
            }

            if (_gifDecoded != null)
            {
                _gifDecoded.Dispose();
                _gifDecoded = null;
            }

            if (_helperFrames != null)
            {
                foreach (Bitmap frame in _helperFrames)
                {
                    if (frame != null)
                        frame.Dispose();
                }
                _helperFrames = null;
            }
        }

        /// <summary>
        /// Applies the memory settings to the currently loaded gif file
        /// </summary>
        public void ApplyMemorySettings()
        {
            if (_gifDecoded == null || !Loaded)
                return;

            _gifDecoded.MaxMemoryForBuffer    = Settings.Instance.MaxBufferMemory * 1024 * 1024;
            _gifDecoded.MaxMemoryForKeyframes = Settings.Instance.MaxKeyframeMemory * 1024 * 1024;
            _gifDecoded.MaxKeyframeReach = Settings.Instance.MaxKeyframeReach;

            _gifDecoded.ApplyMemoryFields();
        }

        /// <summary>
        /// Loads this GIF file's parameters from the given GIF file
        /// </summary>
        /// <param name="path">The gif to load the parameters from</param>
        public void LoadFromPath(string path)
        {
            Loaded = false;
            Dispose();

            // Set the path
            GifPath = path;

            if (LoadFromHelper(path))
            {
                return;
            }

            // Decode the gif file
            _gifDecoded = new GifDecoder(path);
            _gifDecoded.Decode();

            if (_gifDecoded.ConsolidatedState != ErrorState.Ok)
            {
                _gifDecoded.Dispose();
                return;
            }

            // Get information from the gif file
            Width = _gifDecoded.LogicalScreenDescriptor.LogicalScreenSize.Width;
            Height = _gifDecoded.LogicalScreenDescriptor.LogicalScreenSize.Height;

            _currentFrame = 0;
            FrameCount = _gifDecoded.FrameCount;

            // Get frame intervals
            Intervals = new int[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                Intervals[i] = _gifDecoded.GetDelayForFrame(i) * 10;
            }

            // Get whether this GIF loops over:
            CanLoop = (_gifDecoded.NetscapeExtension != null && _gifDecoded.NetscapeExtension.LoopCount == 0);

            // Force load of the first frame
            Image img = _gifDecoded[0].TheImage;

            Gif = new Bitmap(img.Width, img.Height);

            FastBitmap.CopyPixels((Bitmap)_gifDecoded[_currentFrame].TheImage, (Bitmap)Gif);

            Loaded = true;

            ApplyMemorySettings();
        }

        /// <summary>
        /// Returns an interval in ms for the given frame
        /// </summary>
        /// <param name="frame">The frame to get the interval of</param>
        /// <returns>The interval for the frame, in ms</returns>
        public int GetIntervalForFrame(int frame)
        {
            return (Intervals[frame] == 0 ? 1 : Intervals[frame]);
        }

        /// <summary>
        /// Gets the interval in ms for the current frame of this GIF
        /// </summary>
        /// <returns>The interval in ms for the current frame of this GIF</returns>
        public int GetIntervalForCurrentFrame()
        {
            return GetIntervalForFrame(CurrentFrame);
        }

        /// <summary>
        /// Changes the current frame of this GIF file, changing the GIF file's active frame in the process
        /// </summary>
        /// <param name="currentFrame">The new current frame</param>
        public void SetCurrentFrame(int currentFrame)
        {
            if (_currentFrame == currentFrame)
                return;

            _currentFrame = currentFrame;
            _currentInterval = Intervals[currentFrame];
            if (_helperFrames != null)
            {
                FastBitmap.CopyPixels(_helperFrames[currentFrame], (Bitmap)Gif);
            }
            else
            {
                FastBitmap.CopyPixels((Bitmap)_gifDecoded[currentFrame].TheImage, (Bitmap)Gif);
            }
        }

        private bool LoadFromHelper(string path)
        {
            string helper = GetHelperPath();
            if (helper == null)
                return false;

            string tempDir = Path.Combine(Path.GetTempPath(), "gifviewer_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);

                ProcessStartInfo psi = new ProcessStartInfo(helper, "\"" + path + "\" \"" + tempDir + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        return false;
                }

                string[] framePaths = Directory.GetFiles(tempDir, "frame_*.bmp");
                Array.Sort(framePaths, StringComparer.OrdinalIgnoreCase);
                if (framePaths.Length <= 1)
                    return false;

                int[] delays = ReadHelperDelays(tempDir, framePaths.Length);
                Bitmap[] frames = new Bitmap[framePaths.Length];
                for (int i = 0; i < framePaths.Length; i++)
                {
                    frames[i] = LoadBitmap32(framePaths[i]);
                }

                _helperFrames = frames;
                Width = frames[0].Width;
                Height = frames[0].Height;
                _currentFrame = 0;
                FrameCount = frames.Length;
                Intervals = delays;
                _currentInterval = Intervals[0];
                CanLoop = true;
                Gif = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                FastBitmap.CopyPixels(_helperFrames[0], (Bitmap)Gif);
                Loaded = true;
                return true;
            }
            catch
            {
                if (_helperFrames != null)
                {
                    foreach (Bitmap frame in _helperFrames)
                    {
                        if (frame != null)
                            frame.Dispose();
                    }
                    _helperFrames = null;
                }
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch
                {
                }
            }
        }

        private static string GetHelperPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string onedir = Path.Combine(baseDir, "webp_extract_pillow_runtime", "webp_extract_pillow", "webp_extract_pillow.exe");
            if (File.Exists(onedir))
                return onedir;

            string onefile = Path.Combine(baseDir, "webp_extract_pillow.exe");
            if (File.Exists(onefile))
                return onefile;

            return null;
        }

        private static int[] ReadHelperDelays(string tempDir, int frameCount)
        {
            int[] delays = new int[frameCount];
            for (int i = 0; i < delays.Length; i++)
                delays[i] = 100;

            string delayPath = Path.Combine(tempDir, "delays.txt");
            if (!File.Exists(delayPath))
                return delays;

            string[] lines = File.ReadAllLines(delayPath);
            int count = Math.Min(lines.Length, delays.Length);
            for (int i = 0; i < count; i++)
            {
                int delay;
                if (int.TryParse(lines[i], out delay) && delay > 0)
                    delays[i] = delay;
            }

            return delays;
        }

        private static Bitmap LoadBitmap32(string path)
        {
            using (Bitmap source = (Bitmap)Image.FromFile(path))
            {
                Bitmap target = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(target))
                {
                    graphics.DrawImageUnscaled(source, 0, 0);
                }
                return target;
            }
        }

        /// <summary>
        /// Returns the frame count of this Gif file
        /// </summary>
        /// <returns>The frame count of this Gif file</returns>
        public int GetFrameCount()
        {
            return FrameCount;
        }
    }
}
