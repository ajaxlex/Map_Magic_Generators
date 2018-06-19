using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

using MapMagic;

/*
 *   Modified from Denis Pahunov's original MapMagic Custom Shader Output,
 *   with provisions for CTS cutout layer
 *   
 *   THIS SOFTWARE IS PROVIDED "AS IS" AND ANY EXPRESSED OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE REGENTS OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
 *   HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 * 
 *   Have fun!  Ajaxlex
 * 
 */

namespace MapMagic
{
	[GeneratorMenu (menu="Vertexherder", name ="Cutout Custom Shader", disengageable = true, priority = 10, helpLink = "https://gitlab.com/denispahunov/mapmagic/wikis/output_generators/custom_shader")]
	public class CustomShaderOutputCutout  : OutputGenerator
	{
		public enum Mode { Custom, RTP, MegaSplat, CTS };
		public static Mode mode;
		public static bool instantUpdateMaterial = false;

		//custom
		static string[] controlTexturesNames = new string[] { "_ControlTex0" };
		static readonly string[] channelNames = new string[] { "Red", "Green", "Blue", "Alpha" };

		//assets
		#if RTP
		public static ReliefTerrain rtp;
		#endif



		#if CTS_PRESENT
		public static CTS.CTSProfile ctsProfile;

// CUTOUT - various properties exposed for cutout
        public static bool useCutout;
        public Input cutoutIn = new Input(InoutType.Map);
        public Texture2D CutoutTex;
        public Color[] cutoutPixels;
        #endif

#if __MEGASPLAT__
		public static MegaSplatTextureList textureList;
		public static float clusterNoiseScale = 0.05f;

		private string[] clusterNames = new string[0];

		//public Input wetnessIn = new Input(InoutType.Map);
		//public Input puddlesIn = new Input(InoutType.Map);
		//public Input displaceDampenIn = new Input(InoutType.Map);
#endif

        public static bool formatARGB = false;
		public static bool makeNoLongerReadable = false;
		public static bool smoothFallof = false;

		public bool guiMaterial = false;
		public bool guiTexture = false;
		public bool guiLayers = true;

		//layer
		public class Layer
		{
			public Input input = new Input(InoutType.Map);
			public Output output = new Output(InoutType.Map);
			public int index = 0;
			public string name = "Layer";
			public float opacity = 1;

			public void OnAdd (int n) { }
			public void OnRemove (int n) 
			{ 
				input.Link(null,null); 
				Input connectedInput = output.GetConnectedInput(MapMagic.instance.gens.list);
				if (connectedInput != null) connectedInput.Link(null, null);
			}
			public void OnSwitch (int o, int n) { }
		}
		public Layer[] baseLayers = new Layer[0];
		public int selected = 0;

		public void UnlinkBaseLayer (int p, int n)
		{
			if (baseLayers.Length == 0) return;
			if (baseLayers[0].input.link != null) 
				baseLayers[0].input.Link(null, null);
		}
		public void UnlinkBaseLayer (int n) { UnlinkBaseLayer(0,0); }
		
		public void UnlinkLayer (int num)
		{
			Layer layer = baseLayers[num];
			layer.input.Link(null,null); 
			Input connectedInput = layer.output.GetConnectedInput(MapMagic.instance.gens.list);
			if (connectedInput != null) connectedInput.Link(null, null);
		}


		//get static actions using instance
		public override Action<CoordRect, Chunk.Results, GeneratorsAsset, Chunk.Size, Func<float,bool>> GetProces() { return Process; }
		public override Func<CoordRect, Terrain, object, Func<float,bool>, IEnumerator> GetApply() { return Apply; }
		public override Action<CoordRect, Terrain> GetPurge() { return Purge; }


		//generator
		public override IEnumerable<Input> Inputs() 
		{ 
			if (baseLayers==null) baseLayers = new Layer[0];
			for (int i=1; i<baseLayers.Length; i++)  //layer 0 is background
				if (baseLayers[i] != null && baseLayers[i].input != null)
					yield return baseLayers[i].input;

// CUTOUT - return cutout input
            yield return cutoutIn;

            //yield return wetnessIn;
            //yield return puddlesIn;
            //yield return displaceDampenIn;
        }
		public override IEnumerable<Output> Outputs() 
		{ 
			if (baseLayers==null) baseLayers = new Layer[0];
			for (int i=0; i<baseLayers.Length; i++) 
				if (baseLayers[i] != null && baseLayers[i].output != null)
					yield return baseLayers[i].output; 
		}


		public override void Generate(CoordRect rect, Chunk.Results results, Chunk.Size terrainSize, int seed, Func<float,bool> stop= null)
		{
			if ((stop!=null && stop(0)) || !enabled) return;

			//loading inputs
			Matrix[] matrices = new Matrix[baseLayers.Length];
			for (int i = 0; i < baseLayers.Length; i++)
			{
				if (baseLayers[i].input != null)
				{
				   matrices[i] = (Matrix)baseLayers[i].input.GetObject(results);
				   if (matrices[i] != null)
					  matrices[i] = matrices[i].Copy(null);
				}
				if (matrices[i] == null)
				   matrices[i] = new Matrix(rect);
			}
			if (matrices.Length == 0)
				return;

			//background matrix
			//matrices[0] = terrain.defaultMatrix; //already created
			matrices[0].Fill(1);

			//populating opacity array
			float[] opacities = new float[matrices.Length];
			for (int i = 0; i < baseLayers.Length; i++)
				opacities[i] = baseLayers[i].opacity;
			opacities[0] = 1;

			//blending layers
			if (stop!=null && stop(0)) return;
			Matrix.BlendLayers(matrices, opacities);

			//saving changed matrix results
			for (int i = 0; i < baseLayers.Length; i++)
			{
				if (stop!=null && stop(0))
				   return; //do not write object is generating is stopped
				baseLayers[i].output.SetObject(results, matrices[i]);
			}

// CUTOUT - generate initial pixel array from input
            GenerateCutout(rect, results, terrainSize);
		}

// CUTOUT
        private void GenerateCutout(CoordRect rect, Chunk.Results results, Chunk.Size terrainSize)
        {
            Matrix src = (Matrix)cutoutIn.GetObject(results);

            if (src != null)
            {

                Coord min = src.rect.Min;
                Coord max = src.rect.Max;

                int dimension = terrainSize.resolution;

                cutoutPixels = new Color[dimension * dimension];

                for (int x = 0; x < rect.size.x; x++) { 
                    for (int z = 0; z < rect.size.z; z++) {

                        float mask = src[x + src.rect.offset.x, z + src.rect.offset.z];

                        int pos = src.rect.GetPos(x + src.rect.offset.x, z + src.rect.offset.z);
                        cutoutPixels[pos] = new Color(mask, mask, mask, mask);
                    }
                }
            }
        }

        public static void Process (CoordRect rect, Chunk.Results results, GeneratorsAsset gens, Chunk.Size terrainSize, Func<float,bool> stop = null)
		{
			if (mode == Mode.MegaSplat)
				ProcessMegaSplat(rect, results, gens, terrainSize, stop);
			else 
				ProcessControlMaps(rect, results, gens, terrainSize, stop);
		}

		public static void ProcessControlMaps (CoordRect rect, Chunk.Results results, GeneratorsAsset gens, Chunk.Size terrainSize, Func<float,bool> stop = null)
		{
			if (stop!=null && stop(0)) return;

            int colorCount = 4;

            //creating control textures array
            Color[][] colors = new Color[colorCount][];

			//filling arrays
			foreach (CustomShaderOutputCutout gen in MapMagic.instance.gens.GeneratorsOfType<CustomShaderOutputCutout>(onlyEnabled:true, checkBiomes:true))
			{
				//loading biome matrix
				Matrix biomeMask = null;
				if (gen.biome != null)
				{
					object biomeMaskObj = gen.biome.mask.GetObject(results);
					if (biomeMaskObj == null) continue; //adding nothing if biome has no mask
					biomeMask = (Matrix)biomeMaskObj;
					if (biomeMask == null) continue;
					if (biomeMask.IsEmpty()) continue; //optimizing empty biomes
				}

                for (int i=0; i<gen.baseLayers.Length; i++)
				{
					//reading output directly
					Output output = gen.baseLayers[i].output;
					if (stop!=null && stop(0)) return; //checking stop before reading output
					if (!results.results.ContainsKey(output)) continue;
					Matrix matrix = (Matrix)results.results[output];
					if (matrix.IsEmpty()) continue;

					for (int x=0; x<rect.size.x; x++)
						for (int z=0; z<rect.size.z; z++)
					{
						int pos = matrix.rect.GetPos(x+matrix.rect.offset.x, z+matrix.rect.offset.z); //pos should be the same for colors array and matrix array
						
						//get value and adjust with biome mask
						float val = matrix.array[pos];
						float biomeVal = biomeMask!=null? biomeMask.array[pos] : 1;
						val *= biomeVal;

						//save value to colors array
						int arrayNum = gen.baseLayers[i].index / 4;
						int channelNum = gen.baseLayers[i].index % 4;
						switch (channelNum)
						{
							case 0: if (colors[arrayNum] == null) colors[arrayNum] = new Color[rect.size.x*rect.size.z]; colors[arrayNum][pos].r += val; break;
							case 1: if (colors[arrayNum] == null) colors[arrayNum] = new Color[rect.size.x*rect.size.z]; colors[arrayNum][pos].g += val; break;
							case 2: if (colors[arrayNum] == null) colors[arrayNum] = new Color[rect.size.x*rect.size.z]; colors[arrayNum][pos].b += val; break;
							case 3: if (colors[arrayNum] == null) colors[arrayNum] = new Color[rect.size.x*rect.size.z]; colors[arrayNum][pos].a += val; break;
						}
                    }

                    if (stop!=null && stop(0)) return;
				}
            }

            //TODO: normalizing color arrays (if needed)

            //pushing to apply
            if (stop!=null && stop(0)) return;
			results.apply.CheckAdd(typeof(CustomShaderOutputCutout), colors, replace: true);
		}

        public static void ProcessMegaSplat(CoordRect rect, Chunk.Results results, GeneratorsAsset gens, Chunk.Size terrainSize, Func<float,bool> stop = null)
		{
#if __MEGASPLAT__
			if (stop!=null && stop(0)) return;

			//creating color arrays
			Color[][] colors = new Color[2][];

			colors[0] = new Color[MapMagic.instance.resolution * MapMagic.instance.resolution];
			colors[1] = new Color[MapMagic.instance.resolution * MapMagic.instance.resolution];
			
			//creating all and special layers/biomes lists
			List<Layer> allLayers = new List<Layer>(); //all layers count = gen num * layers num in each gen (excluding empty biomes, matrices, etc)
			List<Matrix> allMatrices = new List<Matrix>();
			List<Matrix> allBiomeMasks = new List<Matrix>();

			List<Matrix> specialWetnessMatrices = new List<Matrix>(); //special count = number of generators (excluding empty biomes only)
			//List<Matrix> specialPuddlesMatrices = new List<Matrix>();
			//List<Matrix> specialDampeningMatrices = new List<Matrix>();
			//List<Matrix> specialBiomeMasks = new List<Matrix>();

			//filling all layers/biomes
			foreach (CustomShaderOutputCutout gen in gens.GeneratorsOfType<CustomShaderOutputCutout>(onlyEnabled: true, checkBiomes: true))
			{
				//loading biome matrix
				Matrix biomeMask = null;
				if (gen.biome != null)
				{
					object biomeMaskObj = gen.biome.mask.GetObject(results);
					if (biomeMaskObj == null) continue; //adding nothing if biome has no mask
					biomeMask = (Matrix)biomeMaskObj;
					if (biomeMask == null) continue;
					if (biomeMask.IsEmpty()) continue; //optimizing empty biomes
				}

				for (int i = 0; i < gen.baseLayers.Length; i++)
				{
					//reading output directly
					Output output = gen.baseLayers[i].output;
					if (stop!=null && stop(0)) return; //checking stop before reading output
					if (!results.results.ContainsKey(output)) continue;
					Matrix matrix = (Matrix)results.results[output];
					if (matrix.IsEmpty()) continue;

					if (textureList.clusters == null) 
					{
						Debug.Log("CSO MegaSplat clusters are not assigned");
						continue;
					}
					if (i >= textureList.clusters.Length)
					{
						Debug.LogError("Cluster out of range");
						continue;
					}

					//adding to lists
					allLayers.Add(gen.baseLayers[i]);
					allMatrices.Add(matrix);
					allBiomeMasks.Add(gen.biome == null ? null : biomeMask);
				}

				//adding special
				/*
				object wetnessObj = gen.wetnessIn.GetObject(results);
				specialWetnessMatrices.Add( wetnessObj!=null? (Matrix)wetnessObj : null );

				object puddlesObj = gen.puddlesIn.GetObject(results);
				specialPuddlesMatrices.Add( puddlesObj!=null? (Matrix)puddlesObj : null );

				object dampeingObj = gen.displaceDampenIn.GetObject(results);
				specialDampeningMatrices.Add( dampeingObj!=null? (Matrix)dampeingObj : null );

				specialBiomeMasks.Add(gen.biome == null ? null : biomeMask);
				*/
			}

			//if no texture list found in any of generators - returning
			if (textureList == null || allLayers.Count==0) return;

			//processing
			int allLayersCount = allLayers.Count;
			int specialCount = specialWetnessMatrices.Count;
			for (int x = 0; x<rect.size.x; x++)
				for (int z = 0; z<rect.size.z; z++)
				{
					int pos = rect.GetPos(x + rect.offset.x, z + rect.offset.z);

					// doesn't use height, normal, but I'm not sure how to get that here..
					Vector3 worldPos = new Vector3(
						1f * (x+rect.offset.x) / MapMagic.instance.resolution * rect.size.x,
						0,
						1f * (z+rect.offset.z) / MapMagic.instance.resolution * rect.size.z);
					float heightRatio = results.heights!=null? results.heights.array[pos] : 0.5f; //0 is the bottom point, 1 is the maximum top
					Vector3 normal = new Vector3(0,1,0);

					// find highest two layers
					int botIdx = 0;
					int topIdx = 0;
					float botWeight = 0;
					float topWeight = 0;

					for (int i = 0; i<allLayersCount; i++)
					{
						float val = allMatrices[i].array[pos];
						if (allBiomeMasks[i] != null) val *= allBiomeMasks[i].array[pos];

						// really want world position, Normal, and height ratio for brushes, but for now, just use x/z..

						if (val > botWeight)
						{
							topWeight = botWeight;
							topIdx = botIdx;

							botWeight = val;
							botIdx = i;
						}
						else if (val > topWeight)
						{
							topIdx = i;
							topWeight = val;
						}
					}

					//converting layer index to texture index
					if (allLayers[topIdx].index > textureList.clusters.Length || allLayers[botIdx].index > textureList.clusters.Length)
					{
						allLayers[topIdx].index = 0;
						allLayers[botIdx].index = 0;
					}
						
					topIdx = textureList.clusters[ allLayers[topIdx].index ].GetIndex(worldPos *  clusterNoiseScale, normal, heightRatio);
					botIdx = textureList.clusters[ allLayers[botIdx].index ].GetIndex(worldPos * clusterNoiseScale, normal, heightRatio);

					//swapping indexes to make topIdx always on top
					if (botIdx > topIdx) 
					{
						int tempIdx = topIdx;
						topIdx = botIdx;
						botIdx = tempIdx;

						float tempWeight = topWeight;
						topWeight = botWeight;
						botWeight = tempWeight;
					}

					//finding blend
					float totalWeight = topWeight + botWeight;	if (totalWeight<0.01f) totalWeight = 0.01f; //Mathf.Max and Clamp are slow
					float blend = botWeight / totalWeight;		if (blend>1) blend = 1;

					//adjusting blend curve
					if (smoothFallof) blend = (Mathf.Sqrt(blend) * (1-blend)) + blend*blend*blend;  //Magic secret formula! Inverse to 3*x^2 - 2*x^3

					//setting color
					colors[0][pos] = new Color(botIdx / 255.0f, topIdx / 255.0f, 1.0f - blend, 1.0f);

					//params
					/*for (int i = 0; i<specialCount; i++)
					{
						float biomeVal = specialBiomeMasks[i]!=null? specialBiomeMasks[i].array[pos] : 1;

						if (specialWetnessMatrices[i]!=null) result.param[pos].b = specialWetnessMatrices[i].array[pos] * biomeVal;
						if (specialPuddlesMatrices[i]!=null) 
						{
							result.param[pos].a = specialPuddlesMatrices[i].array[pos] * biomeVal;
							result.param[pos].r = 0.5f;
							result.param[pos].g = 0.5f;
						}
						if (specialDampeningMatrices[i]!=null) result.control[pos].a = specialDampeningMatrices[i].array[pos] * biomeVal;
					}*/
						
				}
			
			//pushing to apply
			if (stop!=null && stop(0))
				return;
			results.apply.CheckAdd(typeof(CustomShaderOutput), colors, replace: true);
#endif
        }


        public static IEnumerator Apply(CoordRect rect, Terrain terrain, object dataBox, Func<float,bool> stop= null)
		{
			//guard if old-style rtp approach is used
			#if RTP
			ReliefTerrain chunkRTP = terrain.gameObject.GetComponent<ReliefTerrain>();
			if (chunkRTP !=null && chunkRTP.enabled) 
			{
				Debug.Log("MapMagic: RTP component on terain chunk detected. RTP Output Generator works with one RTP script assigned to main MM object only. Make sure that Copy Components is turned off.");
				chunkRTP.enabled = false;
			}
			#endif

			//loading objects
			Color[][] colors = (Color[][])dataBox;
			if (colors == null) yield break;

			//finding number of textures
			int maxChannel = 0;
            foreach (CustomShaderOutputCutout gen in MapMagic.instance.gens.GeneratorsOfType<CustomShaderOutputCutout>(onlyEnabled: true, checkBiomes: true)) {
                for (int i = 0; i < gen.baseLayers.Length; i++) {
                    if (gen.baseLayers[i].index > maxChannel) maxChannel = gen.baseLayers[i].index;
                }
            }
			int numTextures = maxChannel / 4 + 1;

			if (mode == Mode.RTP && numTextures > 3) numTextures = 3;
			if (mode == Mode.CTS && numTextures > 4) numTextures = 4;
			if (mode == Mode.MegaSplat) numTextures = 2; //_SplatControl and _SplatParams


            //creating control textures
            Texture2D[] textures = new Texture2D[numTextures];
			for (int i=0; i<textures.Length; i++)
			{
                textures[i] = createTextureFromPixels(colors[i]);
				yield return null;
			}            

            //finding texture names (it will be used in welding)
            string[] texNames = new string[numTextures];
			switch (mode)
			{
				case Mode.Custom: for (int t=0; t<numTextures; t++) texNames[t] = controlTexturesNames[t]; break;
				case Mode.CTS: for (int t=0; t<numTextures; t++) texNames[t] = "_Texture_Splat_" + (t+1); break;
				case Mode.RTP: for (int t=0; t<numTextures; t++) texNames[t] = "_Control" + (t+1); break;
				case Mode.MegaSplat: texNames[0] = "_SplatControl"; texNames[1] = "_SplatParams"; break;
			}

            //welding
            if (MapMagic.instance != null && MapMagic.instance.splatsWeldMargins!=0)
			{
				for (int t=0; t<textures.Length; t++)
				{

					Texture2D tex = textures[t];
					string texName = texNames[t];

                    weldTerrainForTexture(tex, texName, rect);
				}

                // CUTOUT - one extra texture + welding
#if CTS_PRESENT
                Debug.Log("Welding Cutout");
                foreach (CustomShaderOutputCutout gen in MapMagic.instance.gens.GeneratorsOfType<CustomShaderOutputCutout>(onlyEnabled: true, checkBiomes: true))
                {
                    if (mode == Mode.CTS && ctsProfile != null)
                    {
                        gen.CutoutTex = createTextureFromPixels(gen.cutoutPixels);
                        weldTerrainForTexture(gen.CutoutTex, "_Texture_Additional_Masks", rect);
                    }
                }
#endif

                yield return null;
			}	

			//assigning mat copy
			if (Preview.previewOutput == null && MapMagic.instance.customTerrainMaterial != null)
			{
				//duplicating material
				terrain.materialTemplate = null; //need to reset material template to prevent unity crash
				terrain.materialTemplate = new Material(MapMagic.instance.customTerrainMaterial);
				terrain.materialTemplate.name += " (Copy)";

				//refresh rtp (whatever that does)
				#if RTP
				if (mode == Mode.RTP)
				{
					if (rtp == null) rtp = MapMagic.instance.gameObject.GetComponent<ReliefTerrain>();
					if (rtp==null || rtp.globalSettingsHolder==null) yield break;
					rtp.RefreshTextures(terrain.materialTemplate);
					rtp.globalSettingsHolder.Refresh(terrain.materialTemplate, rtp);
				}
				#endif

				//assigning texture
				for (int t=0; t<textures.Length; t++) 
				{
					if (terrain.materialTemplate.HasProperty(texNames[t]))
						terrain.materialTemplate.SetTexture(texNames[t], textures[t]);
					else Debug.Log("MapMagic Custom Shader Output: Material has no property " + texNames[t] + ". Make sure that the right shader is assigned.");
				}

                foreach (CustomShaderOutputCutout gen in MapMagic.instance.gens.GeneratorsOfType<CustomShaderOutputCutout>(onlyEnabled: true, checkBiomes: true))
                {
                    weldTerrainForTexture(gen.CutoutTex, "_Texture_Additional_Masks", rect);

                    if (terrain.materialTemplate.HasProperty("_Texture_Additional_Masks"))
                    {
                        terrain.materialTemplate.SetTexture("_Texture_Additional_Masks", gen.CutoutTex);
                    }
                }

                    //assigning material propery block (not saving for fixed terrains)
                    //#if UNITY_5_5_OR_NEWER
                    //assign textures using material property
                    //MaterialPropertyBlock matProp = new MaterialPropertyBlock();
                    //matProp.SetTexture("_Control1", controlA);
                    //if (controlB!=null) matProp.SetTexture("_Control2", controlB);
                    //#endif

                    //setting CTS base map dist
#if CTS_PRESENT
                if (mode == Mode.CTS && ctsProfile != null) {
                    if (terrain.basemapDistance != ctsProfile.m_globalBasemapDistance)
                    {
                        terrain.basemapDistance = ctsProfile.m_globalBasemapDistance;
                    }
                }
#endif
            }
		}

// REFACTOR TO SUPPORT CUTOUT
        private static void weldTerrainForTexture(Texture2D tex, string texName, CoordRect rect)
        {
            Coord coord = Coord.PickCell(rect.offset, MapMagic.instance.resolution);
            //Chunk chunk = MapMagic.instance.chunks[coord.x, coord.z];

            Chunk neigPrevX = MapMagic.instance.chunks[coord.x - 1, coord.z];
            if (neigPrevX != null && neigPrevX.worker.ready && neigPrevX.terrain.materialTemplate.HasProperty(texName))
            {
                WeldTerrains.WeldTextureToPrevX(tex, (Texture2D)neigPrevX.terrain.materialTemplate.GetTexture(texName));
            }

            Chunk neigNextX = MapMagic.instance.chunks[coord.x + 1, coord.z];
            if (neigNextX != null && neigNextX.worker.ready && neigNextX.terrain.materialTemplate.HasProperty(texName))
            {
                WeldTerrains.WeldTextureToNextX(tex, (Texture2D)neigNextX.terrain.materialTemplate.GetTexture(texName));
            }

            Chunk neigPrevZ = MapMagic.instance.chunks[coord.x, coord.z - 1];
            if (neigPrevZ != null && neigPrevZ.worker.ready && neigPrevZ.terrain.materialTemplate.HasProperty(texName))
            {
                WeldTerrains.WeldTextureToPrevZ(tex, (Texture2D)neigPrevZ.terrain.materialTemplate.GetTexture(texName));
            }

            Chunk neigNextZ = MapMagic.instance.chunks[coord.x, coord.z + 1];
            if (neigNextZ != null && neigNextZ.worker.ready && neigNextZ.terrain.materialTemplate.HasProperty(texName))
            {
                WeldTerrains.WeldTextureToNextZ(tex, (Texture2D)neigNextZ.terrain.materialTemplate.GetTexture(texName));
            }
        }

// REFACTOR TO SUPPORT CUTOUT
        private static Texture2D createTextureFromPixels(Color[] colors)
        {
            Texture2D tex = new Texture2D(MapMagic.instance.resolution, MapMagic.instance.resolution, formatARGB ? TextureFormat.ARGB32 : TextureFormat.RGBA32, false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            //tex.hideFlags = HideFlags.DontSave;
            //tex.filterMode = FilterMode.Point;

            if (colors != null) tex.SetPixels(0, 0, tex.width, tex.height, colors);
            tex.Apply();
            //textures[i] = tex;
            return tex;
        }

        public static void Purge(CoordRect rect, Terrain terrain)
		{
			//purged on switching back to the standard shader
			//TODO: it's wrong, got to be filled with background layer
		}

		public void DrawWarnings (Layout layout)
		{
			if (mode == Mode.RTP)
			{
				if (MapMagic.instance.copyComponents)
				{
					layout.Par(42);
					layout.Label("Copy Component should be turned off to prevent copying RTP to chunks.", rect:layout.Inset(0.8f), helpbox:true);
					if (layout.Button("Fix",rect:layout.Inset(0.2f))) MapMagic.instance.copyComponents = false;
				}

				#if RTP
				if (rtp==null) rtp = MapMagic.instance.GetComponent<ReliefTerrain>();
				if (rtp==null) //if still not found
				{
					layout.Par(42);
					layout.Label("Could not find Relief Terrain component on MapMagic object.", rect:layout.Inset(0.8f), helpbox:true);
					if (layout.Button("Fix",rect:layout.Inset(0.2f))) 
					{
						MeshRenderer renderer = MapMagic.instance.gameObject.GetComponent<MeshRenderer>();
						if (renderer==null) renderer = MapMagic.instance.gameObject.AddComponent<MeshRenderer>();
						renderer.enabled = false;

						rtp = MapMagic.instance.gameObject.AddComponent<ReliefTerrain>();

						//filling empty splats
						Texture2D emptyTex = Extensions.ColorTexture(4,4,new Color(0.5f, 0.5f, 0.5f, 1f));
						emptyTex.name = "Empty";
						rtp.globalSettingsHolder.splats = new Texture2D[] { emptyTex,emptyTex,emptyTex,emptyTex };
					}
				}
				#endif
			}

			if (MapMagic.instance != null && MapMagic.instance.terrainMaterialType != Terrain.MaterialType.Custom)
			{
				layout.Par(30);
				layout.Label("Material Type is not switched to Custom.", rect:layout.Inset(0.8f), helpbox:true);
				if (layout.Button("Fix",rect:layout.Inset(0.2f))) 
				{
					MapMagic.instance.terrainMaterialType = Terrain.MaterialType.Custom;
					foreach (Chunk tw in MapMagic.instance.chunks.All()) tw.SetSettings();
				}
			}

			string shaderName;
			switch (mode)
			{
				case Mode.MegaSplat: shaderName = "MegaSplat"; break;
				case Mode.CTS: shaderName = "CTS"; break;
				case Mode.RTP: shaderName = "ReliefTerrain"; break;
				default: shaderName = ""; break;
			}
			if (MapMagic.instance.customTerrainMaterial == null || (!MapMagic.instance.customTerrainMaterial.shader.name.Contains(shaderName) && mode!=Mode.Custom))
			{
				layout.Par(52);
				layout.Label("No " + shaderName + " material is assigned as Custom Material in Terrain Settings.", rect:layout.Inset(0.8f), helpbox:true);
				if (layout.Button("Fix",rect:layout.Inset(0.2f))) 
				{
					switch (mode)
					{
						case Mode.Custom:
							Shader shader = Shader.Find("Standard");
							MapMagic.instance.customTerrainMaterial = new Material(shader);
							break;
						case Mode.CTS:
							shader = Shader.Find("CTS Terrain Shader Basic");
                            if ( shader == null ) { shader = Shader.Find("CTS/CTS Terrain Shader Basic CutOut"); }
							MapMagic.instance.customTerrainMaterial = new Material(shader);
							#if CTS_PRESENT
							if (ctsProfile != null) UpdateCustomShaderMaterials(); //CTS_ProfileToMaterial(ctsProfile, MapMagic.instance.customTerrainMaterial);
							#endif
							break;
						case Mode.RTP:
							shader = Shader.Find("Relief Pack/ReliefTerrain-FirstPass");
							MapMagic.instance.customTerrainMaterial = new Material(shader);
							break;
						case Mode.MegaSplat:
							shader = Extensions.CallStaticMethodFrom("Assembly-CSharp-Editor", "SplatArrayShaderGUI", "NewShader", null) as Shader;
							MapMagic.instance.customTerrainMaterial = new Material(shader);
							break;
					}


				}
			}

			/*if (!MapMagic.instance.materialTemplateMode)
			{
				layout.Par(30);
				layout.Label("Material Template Mode is off.", rect:layout.Inset(0.8f), helpbox:true);
				if (layout.Button("Fix",rect:layout.Inset(0.2f))) MapMagic.instance.materialTemplateMode = true;
			}*/

			if (MapMagic.instance.assignCustomTerrainMaterial)
			{
				layout.Par(30);
				layout.Label("Assign Custom Material is turned on.", rect:layout.Inset(0.8f), helpbox:true);
				if (layout.Button("Fix",rect:layout.Inset(0.2f))) 
				{
					MapMagic.instance.assignCustomTerrainMaterial = false;
				}
			}

			if (MapMagic.instance.showBaseMap) 
			{
				layout.Par(30);
				layout.Label("Show Base Map is turned on in Settings.", rect:layout.Inset(0.8f), helpbox:true);
				if (layout.Button("Fix",rect:layout.Inset(0.2f))) MapMagic.instance.showBaseMap = false;
			}



			/*if (textureList == null || textureList.clusters == null || textureList.clusters.Length <= 0)
			{
				layout.Par(30);
				layout.Label("Please assign textures and list with clusters below:", rect:layout.Inset(), helpbox:true);

				layout.Field<MegaSplatTextureList>(ref textureList, "TextureList");
				foreach(Input input in Inputs()) input.link = null;
				return;
			}*/
		}

		public override void OnGUI (GeneratorsAsset gens)
		{
			layout.Par(3);
			layout.margin = 4;
			layout.Field(ref mode, "Mode", fieldSize:0.55f);

			//asset/profile
			switch (mode)
			{
				case Mode.Custom:
					
					layout.Par(5);
					layout.Label("Control Textures:");

					for (int i=0; i<controlTexturesNames.Length; i++)
						layout.Field(ref controlTexturesNames[i]);

					layout.Par(5);
					int numTextures = layout.Field(controlTexturesNames.Length, "Total Count:");

					if (numTextures != controlTexturesNames.Length)
						ArrayTools.Resize(ref controlTexturesNames, numTextures, createElementCallback:i => "_ControlTex" + i.ToString());

					break;
						
			
				#if CTS_PRESENT
				case Mode.CTS: layout.Field(ref ctsProfile, "CTS Profile", fieldSize:0.55f); break;
				#endif

				#if __MEGASPLAT__
				case Mode.MegaSplat:
					layout.Field<MegaSplatTextureList>(ref textureList, "TextureList", fieldSize:0.55f);
					layout.Field<float>(ref clusterNoiseScale, "Noise Scale", fieldSize:0.55f);
					break;
				#endif
			}

			layout.margin += 3; layout.rightMargin += 3;

			//material
			layout.Par(5); layout.Foldout(ref guiMaterial, "Material Template");
			if (guiMaterial)
			{
				Rect anchor = layout.lastRect;
				layout.margin += 5;

				layout.Field(ref MapMagic.instance.customTerrainMaterial);

				//if (layout.Button("Save")) layout.SaveAnyAsset(MapMagic.instance.customTerrainMaterial, null, type:"mat");

				//instant update
				if (MapMagic.instance != null)
				{
					MapMagic mapMagic = MapMagic.instance;
					layout.Toggle(ref instantUpdateMaterial, "Instant Update", disabled:!mapMagic.assignCustomTerrainMaterial && mapMagic.terrainMaterialType!=Terrain.MaterialType.Custom);
					if (layout.Button("Update Now", disabled:!mapMagic.assignCustomTerrainMaterial && mapMagic.terrainMaterialType!=Terrain.MaterialType.Custom, monitorChange:false))
						UpdateCustomShaderMaterials();
				}

				layout.margin -= 5;
				layout.Foreground(anchor);
			}


			//texture
			layout.Par(5); layout.Foldout(ref guiTexture, "Control Texture");
			if (guiTexture)
			{
				Rect anchor = layout.lastRect;
				layout.margin += 5;

				layout.Toggle(ref formatARGB, "ARGB Format");
				layout.Toggle(ref makeNoLongerReadable, "Non-readable");
				layout.Toggle(ref smoothFallof, "Smooth Fallof", disabled:mode!=Mode.MegaSplat);

				layout.margin -= 5;
				layout.Foreground(anchor);
			}

			layout.margin += 3; layout.rightMargin += 3;


// CUTOUT - UI support
#if CTS_PRESENT
            if (mode == Mode.CTS && ctsProfile != null)
            {
                //layout.Par(30);
                //layout.Toggle(useCutout, "Use Cutout", monitorChange: false);

                layout.margin += 12; layout.rightMargin += 3;
                layout.Inset(3);
                layout.Par(5);
                layout.Par(40); cutoutIn.DrawIcon(layout, "Input");
                layout.Icon(CutoutTex, rect: layout.Inset(40), frame: true, alphaBlend: false);
                layout.Label("Cutout", rect: layout.Inset(layout.field.width - 80));

                layout.Par(5);
            }
#endif

            layout.margin += 3; layout.rightMargin += 3;

            //layers
            layout.Par(5); 
			layout.Label(mode==Mode.MegaSplat? "Clusters:" : "Layers:");
			//if (guiLayers)
			{
				if (baseLayers.Length == 0) layout.Label("No Layers");

				//refreshing layers
				#if RTP
				if (mode == Mode.RTP && rtp != null)
				{
					Texture2D[] splats = rtp.globalSettingsHolder.splats;
					if (baseLayers.Length != splats.Length) ResetLayers(splats.Length);
				}
				#endif

				#if CTS_PRESENT
				if (mode == Mode.CTS && ctsProfile != null)
				{
					List<CTS.CTSTerrainTextureDetails> textureDetails = ctsProfile.TerrainTextures;
					if (baseLayers.Length != textureDetails.Count) ResetLayers(textureDetails.Count);
				}
				#endif

				#if __MEGASPLAT__
				if (mode == Mode.MegaSplat && textureList != null)
				{
					if (clusterNames.Length != textureList.clusters.Length) clusterNames = new string[textureList.clusters.Length];
					for (int i=0; i<clusterNames.Length; i++)
						clusterNames[i] = textureList.clusters[i].name;
				}
				#endif

				//drawing layers
				layout.margin = 20; layout.rightMargin = 20; layout.fieldSize = 1f;
				for (int i=baseLayers.Length-1; i>=0; i--)
				{
					layout.DrawLayer(OnLayerGUI, ref selected, i);
					//if (layout.DrawWithBackground(OnLayerGUI, active:i==selected, num:i, frameDisabled:false)) selected = i;
				}

				layout.Par(3); layout.Par();
				if (mode == Mode.MegaSplat || mode == Mode.Custom)
				{
					layout.DrawArrayAdd(ref baseLayers, ref selected, layout.Inset(0.25f), reverse:true, createElement:() => new Layer(), onAdded:UnlinkBaseLayer);
					layout.DrawArrayRemove(ref baseLayers, ref selected, layout.Inset(0.25f), reverse:true, onBeforeRemove:UnlinkLayer, onRemoved:UnlinkBaseLayer);
				}
				if (baseLayers.Length > 1)
				{
					layout.DrawArrayDown(ref baseLayers, ref selected, layout.Inset(0.25f), dispUp:true, onSwitch:UnlinkBaseLayer);
					layout.DrawArrayUp(ref baseLayers, ref selected, layout.Inset(0.25f), dispDown:true, onSwitch:UnlinkBaseLayer);
				}
			}
			/*else
			{
				layout.Par();
				for (int i=1; i<baseLayers.Length; i++)
					baseLayers[i].input.DrawIcon(layout);
				layout.Inset(0.1f);
				layout.Label("Inputs", layout.Inset());
			}*/

			//warnings
			layout.Par(5);
			layout.margin = 3; layout.rightMargin = 0;
			if (MapMagic.instance != null) DrawWarnings(layout);
		}

		public void OnLayerGUI (Layout layout, bool selected, int num)
		{
			Layer layer = baseLayers[num];
			if (layer == null) { layer = new Layer(); baseLayers[num] = layer; }

			layout.Par(40); 

			if (num != 0) layer.input.DrawIcon(layout);
			else 
				if (layer.input.link != null) { layer.input.Link(null,null); } 
			
			baseLayers[num].output.DrawIcon(layout);

			if (mode == Mode.Custom)
			{
				layout.Inset(3);
				layout.Icon(null, rect:layout.Inset(40), frame:true, alphaBlend:false);
				
				int texNum = layer.index/4;
				layout.cursor.height = 15;
				texNum = layout.Popup(texNum, controlTexturesNames, rect:layout.Inset(layout.field.width-80));
				
				int chNum = layer.index%4;
				layout.Par(); layout.Inset(40+3);
				chNum = layout.Popup(chNum, channelNames, rect:layout.Inset(layout.field.width-80));
				layout.Par(4);

				layer.index = texNum*4 + chNum;
			}

			#if RTP
			if (mode == Mode.RTP && rtp != null)
			{
				layout.Inset(3);
				layout.Icon(rtp.globalSettingsHolder.splats[layer.index], rect:layout.Inset(40), frame:true, alphaBlend:false);
				layout.Label(rtp.globalSettingsHolder.splats[layer.index].name, rect:layout.Inset(layout.field.width-80));
			}
			#endif

			#if CTS_PRESENT
			if (mode == Mode.CTS && ctsProfile != null)
			{
				layout.Inset(3);
				layout.Icon(ctsProfile.TerrainTextures[layer.index].Albedo, rect:layout.Inset(40), frame:true, alphaBlend:false);
				layout.Label(ctsProfile.TerrainTextures[layer.index].m_name, rect:layout.Inset(layout.field.width-80));
			}
			#endif

			#if __MEGASPLAT__
			if (mode == Mode.MegaSplat && textureList != null)
			{
				layout.Inset(3);
				if (baseLayers[num].index < textureList.clusters.Length) layout.Icon(textureList.clusters[baseLayers[num].index].previewTex, rect:layout.Inset(40), frame:true, alphaBlend:false);
				layer.index = layout.Popup(baseLayers[num].index, clusterNames,rect:layout.Inset(layout.field.width-80));
			}
			#endif

			

			if (num==0)
			{ 
				layout.cursor.y += layout.lineHeight;
				layout.cursor.height -= layout.lineHeight;
				layout.cursor.x -= layout.field.width-80;

				if (layout.cursor.x >= layout.field.x) //in case of rtp not assinged "Background" is displayed out of generator
					layout.Label("Background", rect:layout.Inset(layout.field.width-80), fontSize:9, fontStyle:FontStyle.Italic);
			}
		}

		public void ResetLayers (int newcount)
		{
			for (int i=0; i<baseLayers.Length; i++) 
			{
				baseLayers[i].input.Link(null,null); 

				Input connectedInput = baseLayers[i].output.GetConnectedInput(MapMagic.instance.gens.list);
				if (connectedInput != null) connectedInput.Link(null, null);
			}

			baseLayers = new Layer[newcount];
				
			for (int i=0; i<baseLayers.Length; i++) 
			{
				baseLayers[i] = new Layer();
				baseLayers[i].index = i;
			}
		}


		static public void UpdateCustomShaderMaterials()
		{
			if (MapMagic.instance == null || MapMagic.instance.customTerrainMaterial == null) return;

			//apply profile
			#if CTS_PRESENT
			if (CustomShaderOutputCutout.mode == CustomShaderOutputCutout.Mode.CTS && CustomShaderOutputCutout.ctsProfile != null)
                CustomShaderOutputCutout.CTS_UpdateShader(CustomShaderOutputCutout.ctsProfile, MapMagic.instance.customTerrainMaterial);
			#endif

			#if RTP
			if (mode == Mode.RTP && rtp != null)
				rtp.globalSettingsHolder.Refresh(MapMagic.instance.customTerrainMaterial, rtp);
			#endif
			
			//get control maps shader ids
			int[] texIds;
			switch (CustomShaderOutputCutout.mode)
			{
				case CustomShaderOutputCutout.Mode.CTS: texIds = new int[4]; for (int t=0; t<texIds.Length; t++) texIds[t] = Shader.PropertyToID("_Texture_Splat_" + (t+1)); break;
				case CustomShaderOutputCutout.Mode.RTP: texIds = new int[3]; for (int t=0; t<texIds.Length; t++) texIds[t] = Shader.PropertyToID("_Control_" + (t+1)); break;
				case CustomShaderOutputCutout.Mode.MegaSplat: texIds = new int[2]; texIds[0] = Shader.PropertyToID("_SplatControl"); texIds[1] = Shader.PropertyToID("_SplatParams"); break;
				default: texIds = new int[0]; break;
			}



            foreach (Chunk chunk in MapMagic.instance.chunks.All())
			{
				Material mat = chunk.terrain.materialTemplate;
				if (mat == null) continue;

				//saving control maps
				Texture[] textures = new Texture[texIds.Length];
				for (int t=0; t<texIds.Length; t++)
					if (mat.HasProperty(texIds[t])) textures[t] = mat.GetTexture(texIds[t]);

// CUTOUT - add another texId for the cutout 
#if CTS_PRESENT

                Texture[] cutoutTexture = new Texture[1];
                if (mode == Mode.CTS && ctsProfile != null)
                {
                    var cutoutId = Shader.PropertyToID("_Texture_Additional_Masks");
                    if (mat.HasProperty(cutoutId)) {
                        cutoutTexture[0] = mat.GetTexture(cutoutId);
                    } else
                    {
                        Debug.Log("Fail find cutout");
                    }
                }

#endif


                //copy properties

                //here Unity crashed several times:
                //mat.CopyPropertiesFromMaterial(MapMagic.instance.customTerrainMaterial);  //crashes Unity
                //mat = new Material(MapMagic.instance.customTerrainMaterial); chunk.terrain.materialTemplate = mat;  //crashes Unity too, in some cases

                //here seems to be workaround
                chunk.terrain.materialTemplate = null;
				mat.CopyPropertiesFromMaterial(MapMagic.instance.customTerrainMaterial);
				chunk.terrain.materialTemplate = mat;

				//restoring control maps
				for (int t=0; t<texIds.Length; t++)
					if (mat.HasProperty(texIds[t])) mat.SetTexture(texIds[t], textures[t]);

// CUTOUT - add another texId for the cutout 
#if CTS_PRESENT
                if (mode == Mode.CTS && ctsProfile != null)
                {
                    var cutoutId = Shader.PropertyToID("_Texture_Additional_Masks");
                    if ( mat.HasProperty(cutoutId) ) {
                        Debug.Log("Setting cutout shader update" + cutoutTexture[0].width + " :: " + cutoutTexture[0].height + " :: " + cutoutTexture[0].name);
                        mat.SetTexture(cutoutId, cutoutTexture[0]);

                        var heightId = Shader.PropertyToID("_Remove_Vert_Height");
                        mat.SetInt(heightId, -10000);
                    } else {
                        Debug.Log("Fail re-add cutout");
                    }
                }
#endif


            }
        }

		#if CTS_PRESENT
		static public void CTS_UpdateShader (CTS.CTSProfile m_profile, Material m_material)
		//a static copy of a CompleteTerrainShader.cs UpdateShader method modified the way no cts-object needed
		{
            //Debug.Log("Update shader called");

            //Make sure we have terrain
            //Make sure we have profile
			//skipping :)

            //And albedo tex
            if (m_profile.AlbedosTextureArray == null)
            {
                Debug.LogError("CTS Albedos texture array is missing - rebake textures");
                return;
            }

            //And normal tex
            if (m_profile.NormalsTextureArray == null)
            {
                Debug.LogError("CTS Normals texture array is missing - rebake textures");
                return;
            }

            //And splat tex

            //Exit if unity shader

            //Basemap distance
			//skipping, assigned on apply
 //           if (m_terrain.basemapDistance != m_profile.m_globalBasemapDistance)
 //           {
 //               m_terrain.basemapDistance = m_profile.m_globalBasemapDistance;
                //SetDirty(m_terrain, false, false);
 //           }

            //Albedo's
            m_material.SetTexture("_Texture_Array_Albedo", m_profile.AlbedosTextureArray);

            //Normals
            m_material.SetTexture("_Texture_Array_Normal", m_profile.NormalsTextureArray);

            //Splats
			//skipping, assigned on apply
            //m_material.SetTexture("_Texture_Splat_1", m_splat1);
           // m_material.SetTexture("_Texture_Splat_2", m_splat2);
           // m_material.SetTexture("_Texture_Splat_3", m_splat3);
           // m_material.SetTexture("_Texture_Splat_4", m_splat4);

            //Global settings
            m_material.SetFloat("_UV_Mix_Power", m_profile.m_globalUvMixPower);
            m_material.SetFloat("_UV_Mix_Start_Distance", m_profile.m_globalUvMixStartDistance + UnityEngine.Random.Range(0.001f, 0.003f));
            m_material.SetFloat("_Perlin_Normal_Tiling_Close", m_profile.m_globalDetailNormalCloseTiling);
            m_material.SetFloat("_Perlin_Normal_Tiling_Far", m_profile.m_globalDetailNormalFarTiling);
            m_material.SetFloat("_Perlin_Normal_Power", m_profile.m_globalDetailNormalFarPower);
            m_material.SetFloat("_Perlin_Normal_Power_Close", m_profile.m_globalDetailNormalClosePower);
            m_material.SetFloat("_Terrain_Smoothness", m_profile.m_globalTerrainSmoothness);
            m_material.SetFloat("_Terrain_Specular", m_profile.m_globalTerrainSpecular);
            m_material.SetFloat("_TessValue", m_profile.m_globalTesselationPower);
            m_material.SetFloat("_TessMin", m_profile.m_globalTesselationMinDistance);
            m_material.SetFloat("_TessMax", m_profile.m_globalTesselationMaxDistance);
            m_material.SetFloat("_TessPhongStrength", m_profile.m_globalTesselationPhongStrength);
            m_material.SetFloat("_TessDistance", m_profile.m_globalTesselationMaxDistance);
            m_material.SetInt("_Ambient_Occlusion_Type", (int)m_profile.m_globalAOType);

            //Cutout

            //AO
            if (m_profile.m_globalAOType == CTS.CTSConstants.AOType.None)
            {
                m_material.DisableKeyword("_Use_AO_ON");
                m_material.DisableKeyword("_USE_AO_TEXTURE_ON");
                m_material.SetInt("_Use_AO", 0);
                m_material.SetInt("_Use_AO_Texture", 0);
                m_material.SetFloat("_Ambient_Occlusion_Power", 0f);
            }
            else if (m_profile.m_globalAOType == CTS.CTSConstants.AOType.NormalMapBased)
            {
                m_material.DisableKeyword("_USE_AO_TEXTURE_ON");
                m_material.SetInt("_Use_AO_Texture", 0);
                if (m_profile.m_globalAOPower > 0)
                {
                    m_material.EnableKeyword("_USE_AO_ON");
                    m_material.SetInt("_Use_AO", 1);
                    m_material.SetFloat("_Ambient_Occlusion_Power", m_profile.m_globalAOPower);
                }
                else
                {
                    m_material.DisableKeyword("_USE_AO_ON");
                    m_material.SetInt("_Use_AO", 0);
                    m_material.SetFloat("_Ambient_Occlusion_Power", 0f);
                }
            }
            else
            {
                if (m_profile.m_globalAOPower > 0)
                {
                    m_material.EnableKeyword("_USE_AO_ON");
                    m_material.EnableKeyword("_USE_AO_TEXTURE_ON");
                    m_material.SetInt("_Use_AO", 1);
                    m_material.SetInt("_Use_AO_Texture", 1);
                    m_material.SetFloat("_Ambient_Occlusion_Power", m_profile.m_globalAOPower);
                }
                else
                {
                    m_material.DisableKeyword("_USE_AO_ON");
                    m_material.DisableKeyword("_USE_AO_TEXTURE_ON");
                    m_material.SetInt("_Use_AO", 0);
                    m_material.SetInt("_Use_AO_Texture", 0);
                    m_material.SetFloat("_Ambient_Occlusion_Power", 0f);
                }
            }

            //Global Detail
            if (m_profile.m_globalDetailNormalClosePower > 0f || m_profile.m_globalDetailNormalFarPower > 0f)
            {
                m_material.SetInt("_Texture_Perlin_Normal_Index", m_profile.m_globalDetailNormalMapIdx);
            }
            else
            {
                m_material.SetInt("_Texture_Perlin_Normal_Index", -1);
            }

            //Global Normal Map
            /*if (NormalMap != null)
            {
                m_material.SetFloat("_Global_Normalmap_Power", m_profile.m_globalNormalPower);
                if (m_profile.m_globalNormalPower > 0f)
                {
                    m_material.SetTexture("_Global_Normal_Map", NormalMap);
                }
                else
                {
                    m_material.SetTexture("_Global_Normal_Map", null);
                }
            }
            else*/
            {
                m_material.SetFloat("_Global_Normalmap_Power", 0f);
                m_material.SetTexture("_Global_Normal_Map", null);
            }

            //Colormap settings
            /*if (ColorMap != null)
            {
                m_material.SetFloat("_Global_Color_Map_Far_Power", m_profile.m_colorMapFarPower);
                m_material.SetFloat("_Global_Color_Map_Close_Power", m_profile.m_colorMapClosePower);
                if (m_profile.m_colorMapFarPower > 0f || m_profile.m_colorMapClosePower > 0f)
                {
                    m_material.SetTexture("_Global_Color_Map", ColorMap);
                }
                else
                {
                    m_material.SetTexture("_Global_Color_Map", null);
                }
            }
            else*/
            {
                m_material.SetFloat("_Global_Color_Map_Far_Power", 0f);
                m_material.SetFloat("_Global_Color_Map_Close_Power", 0f);
                m_material.SetTexture("_Global_Color_Map", null);
            }

            //Geological settings
            if (m_profile.GeoAlbedo != null)
            {
                if (m_profile.m_geoMapClosePower > 0f || m_profile.m_geoMapFarPower > 0f)
                {
                    m_material.SetFloat("_Geological_Map_Offset_Close", m_profile.m_geoMapCloseOffset);
                    m_material.SetFloat("_Geological_Map_Close_Power", m_profile.m_geoMapClosePower);
                    m_material.SetFloat("_Geological_Tiling_Close", m_profile.m_geoMapTilingClose);
                    m_material.SetFloat("_Geological_Map_Offset_Far", m_profile.m_geoMapFarOffset);
                    m_material.SetFloat("_Geological_Map_Far_Power", m_profile.m_geoMapFarPower);
                    m_material.SetFloat("_Geological_Tiling_Far", m_profile.m_geoMapTilingFar);
                    m_material.SetTexture("_Texture_Geological_Map", m_profile.GeoAlbedo);
                }
                else
                {
                    m_material.SetFloat("_Geological_Map_Close_Power", 0f);
                    m_material.SetFloat("_Geological_Map_Far_Power", 0f);
                    m_material.SetTexture("_Texture_Geological_Map", null);
                }
            }
            else
            {
                m_material.SetFloat("_Geological_Map_Close_Power", 0f);
                m_material.SetFloat("_Geological_Map_Far_Power", 0f);
                m_material.SetTexture("_Texture_Geological_Map", null);
            }

            //Snow settings
            if (m_profile.m_snowAmount > 0f)
            {
                m_material.SetInt("_Texture_Snow_Index", m_profile.m_snowAlbedoTextureIdx);
                m_material.SetInt("_Texture_Snow_Normal_Index", m_profile.m_snowNormalTextureIdx);
                m_material.SetInt("_Texture_Snow_H_AO_Index", m_profile.m_snowHeightTextureIdx != -1 ? m_profile.m_snowHeightTextureIdx : m_profile.m_snowAOTextureIdx);
                m_material.SetInt("_Texture_Snow_Noise_Index", m_profile.m_snowNoiseTextureIdx);
                m_material.SetFloat("_Snow_Amount", m_profile.m_snowAmount);
                m_material.SetFloat("_Snow_Maximum_Angle", m_profile.m_snowMaxAngle);
                m_material.SetFloat("_Snow_Maximum_Angle_Hardness", m_profile.m_snowMaxAngleHardness);
                m_material.SetFloat("_Snow_Min_Height", m_profile.m_snowMinHeight);
                m_material.SetFloat("_Snow_Min_Height_Blending", m_profile.m_snowMinHeightBlending);
                m_material.SetFloat("_Snow_Noise_Power", m_profile.m_snowNoisePower);
                m_material.SetFloat("_Snow_Noise_Tiling", m_profile.m_snowNoiseTiling);
                m_material.SetFloat("_Snow_Normal_Scale", m_profile.m_snowNormalScale);
                m_material.SetFloat("_Snow_Perlin_Power", m_profile.m_snowDetailPower);
                m_material.SetFloat("_Snow_Tiling", m_profile.m_snowTilingClose);
                m_material.SetFloat("_Snow_Tiling_Far_Multiplier", m_profile.m_snowTilingFar);
                m_material.SetFloat("_Snow_Brightness", m_profile.m_snowBrightness);
                m_material.SetFloat("_Snow_Blend_Normal", m_profile.m_snowBlendNormal);
                m_material.SetFloat("_Snow_Smoothness", m_profile.m_snowSmoothness);
                m_material.SetFloat("_Snow_Specular", m_profile.m_snowSpecular);
                m_material.SetFloat("_Snow_Heightblend_Close", m_profile.m_snowHeightmapBlendClose);
                m_material.SetFloat("_Snow_Heightblend_Far", m_profile.m_snowHeightmapBlendFar);
                m_material.SetFloat("_Snow_Height_Contrast", m_profile.m_snowHeightmapContrast);
                m_material.SetFloat("_Snow_Heightmap_Depth", m_profile.m_snowHeightmapDepth);
                m_material.SetFloat("_Snow_Heightmap_MinHeight", m_profile.m_snowHeightmapMinValue);
                m_material.SetFloat("_Snow_Heightmap_MaxHeight", m_profile.m_snowHeightmapMaxValue);
                m_material.SetFloat("_Snow_Ambient_Occlusion_Power", m_profile.m_snowAOStrength);
                m_material.SetFloat("_Snow_Tesselation_Depth", m_profile.m_snowTesselationDepth);
                m_material.SetVector("_Snow_Color", new Vector4(m_profile.m_snowTint.r * m_profile.m_snowBrightness, m_profile.m_snowTint.g * m_profile.m_snowBrightness, m_profile.m_snowTint.b * m_profile.m_snowBrightness, m_profile.m_snowSmoothness));
                m_material.SetVector("_Texture_Snow_Average", m_profile.m_snowAverage);
            }
            else
            {
                m_material.SetFloat("_Snow_Amount", m_profile.m_snowAmount);
                m_material.SetInt("_Texture_Snow_Index", -1);
                m_material.SetInt("_Texture_Snow_Normal_Index", -1);
                m_material.SetInt("_Texture_Snow_H_AO_Index", -1);
                m_material.SetInt("_Texture_Snow_Noise_Index", -1);
            }

            //Push per texture based settings
            CTS.CTSTerrainTextureDetails td;
            int actualIdx = 0;
            for (int i = 0; i < m_profile.TerrainTextures.Count; i++)
            {
                td = m_profile.TerrainTextures[i];
                actualIdx = i + 1;

                m_material.SetInt(string.Format("_Texture_{0}_Albedo_Index", actualIdx), td.m_albedoIdx);
                m_material.SetInt(string.Format("_Texture_{0}_Normal_Index", actualIdx), td.m_normalIdx);
                m_material.SetInt(string.Format("_Texture_{0}_H_AO_Index", actualIdx), td.m_heightIdx != -1 ? td.m_heightIdx : td.m_aoIdx);

                m_material.SetFloat(string.Format("_Texture_{0}_Tiling", actualIdx), td.m_albedoTilingClose);
                m_material.SetFloat(string.Format("_Texture_{0}_Far_Multiplier", actualIdx), td.m_albedoTilingFar);
                m_material.SetFloat(string.Format("_Texture_{0}_Perlin_Power", actualIdx), td.m_detailPower);
                m_material.SetFloat(string.Format("_Texture_{0}_Snow_Reduction", actualIdx), td.m_snowReductionPower);
                m_material.SetFloat(string.Format("_Texture_{0}_Geological_Power", actualIdx), td.m_geologicalPower);
                m_material.SetFloat(string.Format("_Texture_{0}_Heightmap_Depth", actualIdx), td.m_heightDepth);
                m_material.SetFloat(string.Format("_Texture_{0}_Height_Contrast", actualIdx), td.m_heightContrast);
                m_material.SetFloat(string.Format("_Texture_{0}_Heightblend_Close", actualIdx), td.m_heightBlendClose);
                m_material.SetFloat(string.Format("_Texture_{0}_Heightblend_Far", actualIdx), td.m_heightBlendFar);
                m_material.SetFloat(string.Format("_Texture_{0}_Tesselation_Depth", actualIdx), td.m_heightTesselationDepth);
                m_material.SetFloat(string.Format("_Texture_{0}_Heightmap_MinHeight", actualIdx), td.m_heightMin);
                m_material.SetFloat(string.Format("_Texture_{0}_Heightmap_MaxHeight", actualIdx), td.m_heightMax);
                m_material.SetFloat(string.Format("_Texture_{0}_AO_Power", actualIdx), td.m_aoPower);
                m_material.SetFloat(string.Format("_Texture_{0}_Normal_Power", actualIdx), td.m_normalStrength);
                m_material.SetFloat(string.Format("_Texture_{0}_Triplanar", actualIdx), td.m_triplanar ? 1f : 0f);
                m_material.SetVector(string.Format("_Texture_{0}_Average", actualIdx), td.m_albedoAverage);
                m_material.SetVector(string.Format("_Texture_{0}_Color", actualIdx), new Vector4(td.m_tint.r * td.m_tintBrightness, td.m_tint.g * td.m_tintBrightness, td.m_tint.b * td.m_tintBrightness, td.m_smoothness));
            }

            //And fill out rest as well
            for (int i = m_profile.TerrainTextures.Count; i < 16; i++)
            {
                actualIdx = i + 1;
                m_material.SetInt(string.Format("_Texture_{0}_Albedo_Index", actualIdx), -1);
                m_material.SetInt(string.Format("_Texture_{0}_Normal_Index", actualIdx), -1);
                m_material.SetInt(string.Format("_Texture_{0}_H_AO_Index", actualIdx), -1);
            }


            //Mark the material as dirty
            //SetDirty(m_material, false, false);
        }
		#endif
	}
	
}