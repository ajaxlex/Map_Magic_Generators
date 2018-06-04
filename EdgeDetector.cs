using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MapMagic;

[System.Serializable]
[GeneratorMenu(
    menu = "Vertexherder",
    name = "EdgeDetector",
    disengageable = true,
    helpLink = "http://myGeneratorHelp")]
public class EdgeDetector : Generator
{
    //input and output vars
    public Input input = new Input("Input", InoutType.Map, mandatory: true);
    public Output output = new Output("Output", InoutType.Map);

    private float[,] matrixH;
    private float[,] matrixV;

    //public int kernelSize = 3;

    public enum KernelSize { x3 = 3, x5= 5, x7 = 7, x9 = 9 };
    public KernelSize kernelType = KernelSize.x3;

    //including in enumerator
    public override IEnumerable<MapMagic.Generator.Input> Inputs() { yield return input; }
    public override IEnumerable<MapMagic.Generator.Output> Outputs() { yield return output; }

    public override void Generate( CoordRect rect, Chunk.Results results, Chunk.Size terrainSize, int seed, Func<float, bool> stop = null)
    {
        Matrix src = (Matrix)input.GetObject(results);

        if (stop != null && stop(0)) return;
        if (!enabled) { output.SetObject(results, src); return; }

        Matrix dst = new Matrix(src.rect);

        int kernelSize = (int)kernelType;

        matrixH = new float[kernelSize, kernelSize];
        matrixV = new float[kernelSize, kernelSize];

        buildSobelKernel(kernelSize, ref matrixH, ref matrixV);

        Coord min = src.rect.Min;
        Coord max = src.rect.Max;

        Coord halfSize;
        halfSize.x = matrixH.GetLength(0) / 2;
        halfSize.z = matrixH.GetLength(1) / 2;

        for (int z = min.z; z < max.z; z++)
        {
            for (int x = min.x; x < max.x; x++)
            {
                applyMatrix(x, z, matrixH, src, dst, min, max, halfSize);
            }
        }

        for (int x = min.x; x < max.x; x++)
        {
            for (int z = min.z; z < max.z; z++)
            {
                applyMatrix(x, z, matrixV, src, dst, min, max, halfSize);
            }
        }
        
        if (stop != null && stop(0)) return;
        output.SetObject(results, dst);
    }

    public void applyMatrix( int x, int z, float[,] filter, Matrix src, Matrix dst, Coord min, Coord max, Coord halfSize )
    {
        int nx, nz;
        float result = 0;

        int xC = filter.GetLength(0) / 2;
        int zC = filter.GetLength(1) / 2;

        for (int i = 0; i < filter.GetLength(0); i++)
        {
            for (int j = 0; j < filter.GetLength(1); j++)
            {
                if (filter[i,j] != 0)
                {
                    nx = x + (j - halfSize.x);
                    nz = z + (i - halfSize.z);

                    if (nx >= min.x && nx < max.x && nz >= min.z && nz < max.z)
                    {
                        result += (filter[i,j] * (src[nx,nz]));
                    }
                }
            }
        }
        dst[x, z] += Mathf.Abs(result);
    }

    public static void buildSobelKernel(int size, ref float[,] matrixH, ref float[,] matrixV)
    {
        //int size = n * 2 + 3;
        int half = size / 2;
        int sizeAndHalf = size + half;
        int k = 0;

        for (int i = 0; i < size; i++)
        {
            if ( i <= half )
            {
                k = half + i;     
            } else
            {
                k = sizeAndHalf - i - 1;
            }

            for (int j = 0; j < size; j++)
            {
                if (j < half)
                {
                    matrixH[i,j] = matrixV[j,i] = j - k;
                }
                else if (j > half)
                {
                    matrixH[i,j] = matrixV[j,i] = k - (size - j - 1);
                }
                else
                {
                    matrixH[i,j] = matrixV[j,i] = 0;
                }
            }
        }
    }


    public override void OnGUI(GeneratorsAsset gens)
    {
        layout.Par(20);
        input.DrawIcon(layout);
        output.DrawIcon(layout);
        layout.Field(ref kernelType, "Kernel Size");
    }
}
