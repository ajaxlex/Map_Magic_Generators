using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MapMagic;

[System.Serializable]
[GeneratorMenu(
    menu = "Vertexherder",
    name = "HeightFilter",
    disengageable = true,
    helpLink = "http://myGeneratorHelp")]
public class HeightFilter : Generator
{
    public float heightMin = 0;
    public float heightMax = 1;

    //input and output vars
    public Input input = new Input("Input", InoutType.Map, mandatory: true);
    public Output output = new Output("Output", InoutType.Map);

    //including in enumerator
    public override IEnumerable<MapMagic.Generator.Input> Inputs() { yield return input; }
    public override IEnumerable<MapMagic.Generator.Output> Outputs() { yield return output; }

    public override void Generate(CoordRect rect, Chunk.Results results, Chunk.Size terrainSize, int seed, Func<float, bool> stop = null)
    {
        Matrix src = (Matrix)input.GetObject(results);

        if (stop != null && stop(0)) return;
        if (!enabled) { output.SetObject(results, src); return; }

        Matrix dst = new Matrix(src.rect);

        float hMin = heightMin / terrainSize.height;
        float hMax = heightMax / terrainSize.height;

        Coord min = src.rect.Min;
        Coord max = src.rect.Max;

        for (int x = min.x; x < max.x; x++)
        {
            for (int z = min.z; z < max.z; z++)
            {
                dst[x, z] = (src[x, z] >= hMin && src[x, z] <= hMax) ? 1 : 0;
            }
        }

        if (stop != null && stop(0)) return;
        output.SetObject(results, dst);
    }

    public void applyMatrix(int x, int z, float[,] filter, Matrix src, Matrix dst)
    {
        int nx, nz;
        float result = 0;

        int xC = filter.GetLength(0) / 2;
        int zC = filter.GetLength(1) / 2;

        Coord min = src.rect.Min;
        Coord max = src.rect.Max;

        for (int i = 0; i < filter.GetLength(0); i++)
        {
            for (int j = 0; j < filter.GetLength(1); j++)
            {
                if (filter[i, j] != 0)
                {
                    nx = x + (j - xC);
                    nz = z + (i - zC);

                    if (nx >= min.x && nx < max.x && nz >= min.z && nz < max.z)
                    {
                        result += (filter[i, j] * (src[nx, nz]));
                    }
                }
            }
        }

        dst[x, z] += Mathf.Abs(result);
    }


    public override void OnGUI(GeneratorsAsset gens)
    {
        layout.Par(20);
        input.DrawIcon(layout);
        output.DrawIcon(layout);
        layout.Field(ref heightMin, "Height Min");
        layout.Field(ref heightMax, "Height Max");
    }
}
