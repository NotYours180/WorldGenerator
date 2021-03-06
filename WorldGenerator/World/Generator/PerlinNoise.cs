﻿using System;

namespace Sean.WorldGenerator
{
    // see http://flafla2.github.io/2014/08/09/perlinnoise.html

	public class PerlinNoise
	{
        public PerlinNoise(int seed, int unitSize = 100)
        {
            WorldSeed = seed;
            PerlinUnitSize = unitSize;
        }

        private Array<float> GenerateSmoothNoise(Array<float> baseNoise, int octave)
		{
            var smoothNoise = new Array<float> (baseNoise.Size);
            int samplePeriod = 1 << (octave + baseNoise.Size.scale); // calculates 2 ^ k
			float sampleFrequency = 1.0f / samplePeriod;

            for (int i = baseNoise.Size.minZ; i < baseNoise.Size.maxZ; i += baseNoise.Size.scale) 
			{
				//calculate the horizontal sampling indices
				int iSample0 = (i / samplePeriod) * samplePeriod;
                int iSample1 = (iSample0 + samplePeriod);
                if (iSample1 > baseNoise.Size.maxZ) iSample1 = iSample1 - baseNoise.Size.zWidth; //wrap around
				float horizontalBlend = (i - iSample0) * sampleFrequency;

                for (int j = baseNoise.Size.minX; j < baseNoise.Size.maxX; j += baseNoise.Size.scale) 
				{
					//calculate the vertical sampling indices
					int jSample0 = (j / samplePeriod) * samplePeriod;
                    int jSample1 = (jSample0 + samplePeriod);
                    if (jSample1 > baseNoise.Size.maxX) jSample1 = jSample1 - baseNoise.Size.xHeight; //wrap around
					float verticalBlend = (j - jSample0) * sampleFrequency;

					//blend the top two corners
                    float top = Lerp(baseNoise[jSample0,iSample0], baseNoise[jSample0,iSample1], horizontalBlend);

					//blend the bottom two corners
                    float bottom = Lerp(baseNoise[jSample1,iSample0], baseNoise[jSample1,iSample1], horizontalBlend);

					//final blend
                    smoothNoise.Set(j,i,Lerp(top, bottom, verticalBlend));
				}
			}
			return smoothNoise;
		}

        public Array<int> GetIntMap(ArraySize size, int octaveCount = 3, double persistence = 0.5)
        {
            var noise = new Array<int>(size);
            for (int z = size.minZ; z < size.maxZ; z += size.scale)
            {
                for (int x = size.minX; x < size.maxX; x += size.scale)
                {
                    double height = OctavePerlin (size, x,1,z, octaveCount, persistence);
                    noise.Set (x, z, (int)(height * size.maxY));
                }
            }
            return noise;
        }

        public Array<float> GetFloatMap(ArraySize size, int octaveCount)
        {
            var noise = new Array<float>(size);
            for (int z = size.minZ; z < size.maxZ; z += size.scale)
            {
                for (int x = size.minX; x < size.maxX; x += size.scale) {
                    double height = OctavePerlin (noise.Size, x, 1, z, octaveCount, 0.5);
                    noise.Set (x, z, (float)(height * size.maxY));
                }
            }
            return noise;
        }

        public double OctavePerlin(ArraySize size, int x, int y, int z, int octaves, double persistence) {
            double xf = (double)x / PerlinUnitSize;
            double yf = 0.0;
            double zf = (double)z / PerlinUnitSize;
            double total = 0;
            int frequency = 1;
            double amplitude = 1;
            double maxValue = 0;            // Used for normalizing result to 0.0 - 1.0
            for(int i=0;i<octaves;i++) {
                total += Perlin(size, xf*frequency, yf*frequency, zf*frequency, i) * amplitude;

                maxValue += amplitude;

                amplitude *= persistence;
                frequency *= 2;
            }

            return total/maxValue;
        }

        private int p(int x, int y, int z, int octave) {
            return (int)(Misc.GetDeterministicInt (x, y, z, octave, WorldSeed) % 256);
        }

        public double Perlin(ArraySize size, double x, double y, double z, int octave) {
            int xi = (int)x;// & 255;                     // Calculate the "unit cube" that the point asked will be located in
            int yi = (int)y;// & 255;                     // The left bound is ( |_x_|,|_y_|,|_z_| ) and the right bound is that
            int zi = (int)z;// & 255;                     // plus 1.  Next we calculate the location (from 0.0 to 1.0) in that cube.
            double xf = Math.Round(x-Math.Floor(x),15);                         // We also fade the location to smooth the result.
            double yf = Math.Round(y-Math.Floor(y),15);
            double zf = Math.Round(z-Math.Floor(z),15);

            int aaa, aba, aab, abb, baa, bba, bab, bbb;
            aaa = p(    xi ,    yi ,    zi ,octave);
            aba = p(    xi,  (yi+1),    zi ,octave);
            aab = p(    xi ,    yi , (zi+1),octave);
            abb = p(    xi , (yi+1), (zi+1),octave);
            baa = p( (xi+1),    yi ,    zi ,octave);
            bba = p( (xi+1), (yi+1),    zi ,octave);
            bab = p( (xi+1),    yi , (zi+1),octave);
            bbb = p( (xi+1), (yi+1), (zi+1),octave);

            double u = xf;//fade(xf);
            double v = yf;//fade(yf);
            double w = zf;//fade(zf);

            double x1, x2, y1, y2;
            x1 = Lerp(    Grad (aaa, xf  , yf  , zf),   // The gradient function calculates the dot product between a pseudorandom
                Grad (baa, xf-1, yf  , zf),             // gradient vector and the vector from the input coordinate to the 8
                u);                                     // surrounding points in its unit cube.
            x2 = Lerp(    Grad (aba, xf  , yf-1, zf),   // This is all then lerped together as a sort of weighted average based on the faded (u,v,w)
                Grad (bba, xf-1, yf-1, zf),             // values we made earlier.
                u);
            y1 = Lerp(x1, x2, v);

            x1 = Lerp(    Grad (aab, xf  , yf  , zf-1),
                Grad (bab, xf-1, yf  , zf-1),
                u);
            x2 = Lerp(    Grad (abb, xf  , yf-1, zf-1),
                Grad (bbb, xf-1, yf-1, zf-1),
                u);
            y2 = Lerp (x1, x2, v);

            return (Lerp (y1, y2, w)+1)/2;               // For convenience we bind the result to 0 - 1 (theoretical min/max before is [-1, 1])
        }

        public static double Grad(int hash, double x, double y, double z) {
            switch(hash & 0xF)
            {
            case 0x0: return  x + y;
            case 0x1: return -x + y;
            case 0x2: return  x - y;
            case 0x3: return -x - y;
            case 0x4: return  x + z;
            case 0x5: return -x + z;
            case 0x6: return  x - z;
            case 0x7: return -x - z;
            case 0x8: return  y + z;
            case 0x9: return -y + z;
            case 0xA: return  y - z;
            case 0xB: return -y - z;
            case 0xC: return  y + x;
            case 0xD: return -y + z;
            case 0xE: return  y - x;
            case 0xF: return -y - z;
            default: return 0; // never happens
            }
        }

        private float Lerp(float a, float b, float w)
        {
            return a + w * (b - a);
        }
        public double Lerp(double a, double b, double w)
        {
            return a + w * (b - a);
        }

        private int Lerp(int minY, int maxY, float t)
        {
            float u = 1 - t;
            return (int)(minY * u + maxY * t);
        }

        private double Fade(double t)
        {
            // Fade function as defined by Ken Perlin.  This eases coordinate values
            // so that they will "ease" towards integral values.  This ends up smoothing
            // the final output.
            return t * t * t * (t * (t * 6 - 15) + 10);         // 6t^5 - 15t^4 + 10t^3
        }

        public int WorldSeed { get; set; }

        public int PerlinUnitSize { get; set; }
    }
}