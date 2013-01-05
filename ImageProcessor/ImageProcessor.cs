﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ParallelArrays;

namespace ImageProcessing
{
    public class ImageProcessor
    {
        #region "Accelerator stuff"
        public static DX9Target _dx9Target = new DX9Target();
        public static MulticoreTarget _multicoreTarget = new MulticoreTarget();
        public enum AcceleratorTarget
        {
            DX9,
            Multicore
        }
        public static Target GetAcceleratorTarget(AcceleratorTarget acceleratorTarget)
        {
            if (acceleratorTarget == AcceleratorTarget.DX9)
                return _dx9Target;
            else
                return _multicoreTarget;
        }
        #endregion

        // -------------- Average and Maximum --------------

        // This method that takes YData instead of YDataFloat is by far the fastest method.
        public static void CalculateAverageAndMaximumCPU(YData imageData, out float average, out float maximum)
        {
            int width = imageData.Width;
            int height = imageData.Height;

            // Calculate average and maximum
            average = 0.0f;
            maximum = 0.0f;

            var averageSlices = new int[height];
            var maximumSlices = new byte[height];
            Parallel.For(0, height - 1, (y) =>
            {
                unsafe
                {
                    fixed (byte* pDataByte = imageData.Data)
                    {
                        var pData = (int*)pDataByte; // Tried long* instead of int* and it made little difference

                        int ySrcOffset = (y * imageData.Stride) / sizeof(int);
                        int xMax = width / sizeof(int);

                        int avg = 0;
                        byte max = 0;
                        for (int x = 0; x < xMax; x++)
                        {
                            var color = pData[ySrcOffset++];

                            byte value;

                            value = (byte)(color & 0xFF);
                            avg += value;
                            if (max < value)
                                max = value;

                            value = (byte)((color >> 8) & 0xFF);
                            avg += value;
                            if (max < value)
                                max = value;

                            value = (byte)((color >> 16) & 0xFF);
                            avg += value;
                            if (max < value)
                                max = value;

                            value = (byte)((color >> 24) & 0xFF);
                            avg += value;
                            if (max < value)
                                max = value;
                        }

                        averageSlices[y] = avg;
                        maximumSlices[y] = max;
                    }
                }
            });

            average = averageSlices.AsParallel().Sum();
            average /= (float)(width * height);

            maximum = maximumSlices.AsParallel().Max();
        }

        public static void CalculateAverageAndMaximumCPU(YDataFloat imageData, out float average, out float maximum)
        {
            int width = imageData.Width;
            int height = imageData.Height;

            // Calculate average and maximum
            average = 0.0f;
            maximum = 0.0f;

            var averageSlices = new float[height];
            var maximumSlices = new float[height];
            Parallel.For(0, height - 1, (y) =>
            {
                float avg = 0.0f;
                float max = 0.0f;
                for (int x = 0; x < width; x++)
                {
                    var value = imageData.Data[x, y];
                    avg += value;
                    if (max < value)
                        max = value;
                }
                averageSlices[y] = avg;
                maximumSlices[y] = max;
            });

            average = averageSlices.AsParallel().Sum();
            average /= (float)(width * height);

            maximum = maximumSlices.AsParallel().Max();
        }
        
        public static void CalculateAverageAndMaximumAccel(AcceleratorTarget acceleratorTarget, float[,] imageData, out float average, out float maximum)
        {
            var target = GetAcceleratorTarget(acceleratorTarget);

            int width = imageData.GetLength(0);
            int height = imageData.GetLength(1);

            var fpInput = new FloatParallelArray(imageData);
            
            var fpAverage = ParallelArrays.Sum(fpInput);
            fpAverage = ParallelArrays.Divide(fpAverage, (float)(width * height));
            fpAverage = ParallelArrays.Pad(fpAverage, new int[] { 0 }, new int[] { 1 }, 0.0f );

            var fpMaximum = ParallelArrays.MaxVal(fpInput);
            fpMaximum = ParallelArrays.Pad(fpMaximum, new int[] { 1 }, new int[] { 0 }, 0.0f);
            
            var fpOutput = ParallelArrays.Add(fpAverage, fpMaximum);

            var output = target.ToArray1D(fpOutput);

            average = output[0];
            maximum = output[1];
        }

        // -------------- Standard Deviation --------------

        public static void CalculateStandardDeviationCPU(YData imageData, float average, out float stdDev)
        {
            int width = imageData.Width;
            int height = imageData.Height;

            // Calculate standard deviation
            stdDev = 0.0f;

            float[] totalSlices = new float[height];

            //for (int y = 0; y < height; y++)
            Parallel.For(0, height - 1, (y) =>
            {
                unsafe
                {
                    fixed (byte* pDataByte = imageData.Data)
                    {
                        var pData = (int*)pDataByte; // Tried long* instead of int* and it was far worse performance

                        int ySrcOffset = (y * imageData.Stride) / sizeof(int);
                        int xMax = width / sizeof(int);

                        float total = 0.0f;

                        for (int x = 0; x < xMax; x++)
                        {
                            var color = pData[ySrcOffset++];
                            float value;

                            value = (float)(color & 0xFF);
                            value = (value - average) * (value - average);
                            total += value;

                            value = (float)((color >> 8) & 0xFF);
                            value = (value - average) * (value - average);
                            total += value;

                            value = (float)((color >> 16) & 0xFF);
                            value = (value - average) * (value - average);
                            total += value;

                            value = (float)((color >> 24) & 0xFF);
                            value = (value - average) * (value - average);
                            total += value;
                        }
                        totalSlices[y] = total;
                    }
                }
            });

            stdDev = totalSlices.AsParallel().Sum();
            stdDev /= (float)(width * height);
            stdDev = (float)Math.Sqrt(stdDev);
        }

        public static void CalculateStandardDeviationCPU(YDataFloat imageData, float average, out float stdDev)
        {
            int width = imageData.Width;
            int height = imageData.Height;

            // Calculate standard deviation
            stdDev = 0.0f;

            float[] totalSlices = new float[height];
            Parallel.For(0, height - 1, (y) =>
            {
                unsafe
                {
                    fixed (float* pFloat = imageData.Data)
                    {
                        int ySrcOffset = y;

                        float total = 0.0f;
                        for (int x = 0; x < width; x++)
                        {
                            var value = pFloat[ySrcOffset];
                            ySrcOffset += height;

                            value = (value - average) * (value - average);
                            total += value;
                        }
                        totalSlices[y] = total;
                    }
                }
            });

            stdDev = totalSlices.AsParallel().Sum();
            stdDev /= (float)(width * height);
            stdDev = (float)Math.Sqrt(stdDev);
        }

        // -------------- Edge detection --------------

        //"02.6656726 - Edge Detect CPU Single Threaded
        //"01.1025473 - Edge Detect CPU Parallel
        //"02.1731188 - Edge Detect Accel Multicore
        //"00.7777913 - Edge Detect Accel DX9

        public static YDataFloat GenerateEdgeDetectedImageSingleThreaded(YDataFloat imageData)
        {
            var width = imageData.Width;
            var height = imageData.Height;

            var edgeData = new float[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if ((y < height - 1) && (x < width - 1))
                    {
                        edgeData[x, y] = imageData.Data[x + 1, y] + imageData.Data[x, y + 1] - imageData.Data[x, y] - imageData.Data[x, y];
                        edgeData[x, y] /= 2.0f;
                        edgeData[x, y] += (imageData.MaximumValue / 2.0f);
                        if (edgeData[x, y] < 0.0f)
                            edgeData[x, y] = 0.0f;
                        if (edgeData[x, y] > imageData.MaximumValue)
                            edgeData[x, y] = imageData.MaximumValue;
                    }
                    else
                    {
                        edgeData[x, y] = 0.0f;
                    }
                }
            }

            return new YDataFloat(edgeData, imageData.MaximumValue);
        }

        public static YDataFloat GenerateEdgeDetectedImage(YDataFloat imageData)
        {
            var width = imageData.Width;
            var height = imageData.Height;

            var edgeData = new float[width, height];

            Parallel.For(0, height - 1, (y) =>
            {
                for (int x = 0; x < width; x++)
                {
                    if ((y < height - 1) && (x < width - 1))
                    {
                        edgeData[x, y] = imageData.Data[x + 1, y] + imageData.Data[x, y + 1] - imageData.Data[x, y] - imageData.Data[x, y];
                        edgeData[x, y] /= 2.0f;
                        edgeData[x, y] += (imageData.MaximumValue / 2.0f);
                        if (edgeData[x, y] < 0.0f)
                            edgeData[x, y] = 0.0f;
                        if (edgeData[x, y] > imageData.MaximumValue)
                            edgeData[x, y] = imageData.MaximumValue;
                    }
                    else
                    {
                        edgeData[x, y] = 0.0f;
                    }
                }
            });

            return new YDataFloat(edgeData, imageData.MaximumValue);
        }

        public static YDataFloat GenerateEdgeDetectedImageAccel(AcceleratorTarget acceleratorTarget, YDataFloat imageData)
        {
            var target = GetAcceleratorTarget(acceleratorTarget);

            var width = imageData.Width;
            var height = imageData.Height;

            var fpInput = new FloatParallelArray(imageData.Data);

            var fpInputX = ParallelArrays.Shift(fpInput, new int[] { 1, 0 });
            var fpInputY = ParallelArrays.Shift(fpInput, new int[] { 0, 1 });

            var fpDX = ParallelArrays.Subtract(fpInputX, fpInput);
            var fpDY = ParallelArrays.Subtract(fpInputY, fpInput);
            var fpTotals = ParallelArrays.Add(fpDX, fpDY);

            var fpOutput = ParallelArrays.Divide(fpTotals, 2.0f);
            fpOutput = ParallelArrays.Add(fpOutput, imageData.MaximumValue / 2.0f);
            fpOutput = ParallelArrays.Max(fpOutput, 0.0f);
            fpOutput = ParallelArrays.Min(fpOutput, imageData.MaximumValue);

            var output = target.ToArray2D(fpOutput);

            return new YDataFloat(output, imageData.MaximumValue);
        }


        // -------------- Average of Edges --------------

        public static YDataFloat GenerateEdgeDetectionAverage(IEnumerable<YDataFloat> edgeMapList)
        {
            var firstEdgeMap = edgeMapList.First();

            var width = firstEdgeMap.Width;
            var height = firstEdgeMap.Height;
            var maximum = firstEdgeMap.MaximumValue;

            // Calculate averages first
            var averageArray = new float[width, height];
            Array.Clear(averageArray, 0, averageArray.Length);

            var edgeMapCount = 0;

            unsafe
            {
                fixed (float* pAverage = averageArray)
                {
                    foreach (var edgeMap in edgeMapList)
                    {
                        fixed (float* pFloat = edgeMap.Data)
                        {
                            int size = width * height;
                            for (int i = 0; i < size; i++)
                            {
                                pAverage[i] += pFloat[i];
                            }
                        }
                        edgeMapCount++;
                    }
                }
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    averageArray[x, y] /= (float)edgeMapCount;
                }
            }

            return new YDataFloat(averageArray, maximum);
        }

        public static YDataFloat GenerateEdgeDetectionAverageAccel(AcceleratorTarget acceleratorTarget, IEnumerable<YDataFloat> edgeMapList)
        {
            var target = GetAcceleratorTarget(acceleratorTarget);

            var firstEdgeMap = edgeMapList.First();

            var width = firstEdgeMap.Width;
            var height = firstEdgeMap.Height;
            var maximum = firstEdgeMap.MaximumValue;

            FloatParallelArray fpTotals = null;
            var edgeMapCount = 0;

            foreach (var edgeMap in edgeMapList)
            {
                var fpInput = new FloatParallelArray(edgeMap.Data);

                if (fpTotals == null)
                    fpTotals = fpInput;
                else
                    fpTotals = ParallelArrays.Add(fpTotals, fpInput);

                edgeMapCount++;
            }

            var fpOutput = ParallelArrays.Divide(fpTotals, edgeMapCount);

            var output = target.ToArray2D(fpOutput);

            return new YDataFloat(output, maximum);
        }

        
        // -------------- Std Deviation of Edges --------------

        public static YDataFloat GenerateEdgeDetectionStandardDeviation(IEnumerable<YDataFloat> edgeMapList)
        {
            var firstEdgeMap = edgeMapList.First();

            var width = firstEdgeMap.Width;
            var height = firstEdgeMap.Height;
            var maximum = firstEdgeMap.MaximumValue;

            var averageArray = GenerateEdgeDetectionAverage(edgeMapList).Data;

            // Now calculate Std Dev
            var stdDevArray = new float[width, height];
            Array.Clear(stdDevArray, 0, averageArray.Length);

            var edgeMapCount = 0;

            foreach (var edgeMap in edgeMapList)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var value = edgeMap.Data[x, y];
                        var average = averageArray[x, y];
                        value = (value - average) * (value - average);
                        stdDevArray[x, y] += value;
                    }
                }
                edgeMapCount++;
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    stdDevArray[x, y] /= (float)edgeMapCount;
                    stdDevArray[x, y] = (float)Math.Sqrt(stdDevArray[x, y]);
                }
            }

            return new YDataFloat(stdDevArray, maximum);
        }

        // -------------- Frame difference, for logo detection --------------

        public static void CalculateLogoDifference(YDataFloat edgeMapAvg, YDataFloat logoDetectionFrame, out float difference)
        {
            int width = edgeMapAvg.Width;
            int height = edgeMapAvg.Height;

            // Calculate absolute difference between values

            var total = 0.0f;
            Parallel.For(0, height - 1, (y) =>
            {
                unsafe
                {
                    fixed (float* pEdgeMapAvg = edgeMapAvg.Data)
                    {
                        fixed (float* pLogoDetectionFrame = logoDetectionFrame.Data)
                        {
                            int size = width * height;
                            for (int i = 0; i < size; i++)
                            {
                                total += Math.Abs(pEdgeMapAvg[i] - pLogoDetectionFrame[i]);
                            }
                        }
                    }
                }
            });

            total /= (float)(width * height);
            difference = total;
        }
    
    }
}
