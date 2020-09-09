using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RomanPort.HttpWebArchive.Framework.Util
{
    public static class AudioWavformGenerator
    {
        public static async Task CreateImage(Stream output, byte[] data, int padding, int height, int decim, float cornerRadius, Rgba32 foregroundColor, Rgba32 backgroundColor)
        {
            //Loop through data and find the max
            int max = 0;
            for (int i = 0; i < data.Length; i += 1)
                max = Math.Max(data[i], max);
            
            //Deteremine multiplication factor for data
            float scale = (float)height / max / 2f;

            //Get image center
            int center = padding + (height / 2);

            //Get number of data points
            int dataPoints = data.Length / decim;

            //Create image
            using (Image<Rgba32> img = new Image<Rgba32>(dataPoints + padding + padding, height + padding + padding))
            {
                //Set background
                IPathCollection corners = BuildCorners(img.Width, img.Height, cornerRadius);
                img.Mutate( x => {
                    foreach (var c in corners) {
                        x.Fill(backgroundColor, c);
                    }
                });
                
                //Loop through data and draw
                for(int i = 0; i<dataPoints; i+=1)
                {
                    //Get data
                    float point = data[i * decim];
                    for (int j = 1; j < decim; j++)
                        point = (data[(i * decim) + j] + point) / 2;

                    //Scale data
                    point *= scale;

                    //Draw from the center outward
                    for(int h = 0; h<point; h+=1)
                    {
                        img[i + padding, center + h] = foregroundColor;
                        img[i + padding, center - h] = foregroundColor;
                    }
                }

                //Output
                await img.SaveAsPngAsync(output);
            }
        }

        private static IPathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius)
        {
            if(cornerRadius <= 0)
            {
                return new PathCollection(new RectangularPolygon(0, 0, imageWidth, imageHeight));
            } else
            {
                return new PathCollection(new EllipsePolygon(cornerRadius, cornerRadius, cornerRadius),
                    new EllipsePolygon(imageWidth - cornerRadius, cornerRadius, cornerRadius),
                    new EllipsePolygon(imageWidth - cornerRadius, imageHeight - cornerRadius, cornerRadius),
                    new EllipsePolygon(cornerRadius, imageHeight - cornerRadius, cornerRadius),
                    new RectangularPolygon(cornerRadius, 0, imageWidth - cornerRadius - cornerRadius, imageHeight),
                    new RectangularPolygon(0, cornerRadius, imageWidth, imageHeight - cornerRadius - cornerRadius));
            }
        }
    }
}
