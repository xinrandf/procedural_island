using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System;

// Pop this script into the component menu so it's easy to find.
[AddComponentMenu("Island Generator/Island Generator")]
public class IslandGenerator : MonoBehaviour {

    public Terrain terrain;

    // Stuff for the cellular automata magic
    private int smoothTimes = 5;
    private int neighboringWalls = 4;

    // Max radius for the shore effect
    public int maxRadius;
    // This one actually gets used for calculations. The first radius is for the GUI (min 20, max 100),
    // but depending on resolution, the real radius can be bigger or smaller. So we keep this extra variable
    // to avoid messing up the original value.
    private int maxRadius2;

    // Scaled down map size for calculations
    private int width;
    private int height;

    // How much we're scaling the map
    private int scaleHeightMultip;
    private int scaleWidthMultip;

    private int terrainreso;

    // Temporary map size for different scaling steps
    // These, plus the two above, help when making the basemap with cellular automata.
    // We shrink the original size a bit to get nicer results.
    private int scaledWidth;
    private int scaledHeight;

    public Thread th1;

    // Flip this to true if you want to stop Thread th1.
    public bool aborted = false;

    // Tracks every pixel in the terrain and how many we've processed so far.
    // Lets us know when the shore calculation is done.
    private int gridSize;
    private int calculatedPixels;

    // How far along the shore calculation is
    public string prog;

    // The original map size
    private int terrainDataMapHeight;
    private int terrainDataMapWidth;

    // Perlin noise settings
    public float perlinHeight = 0.02f;
    public float perlinScale = 20.0f;

    public int mapSmoothTimes = 10;

    // Seed for random stuff, and whether we want a random seed
    public string seed;
    public bool useRandomSeed = true;

    public List<string> usedSeeds = new List<string>(0);

    // How much random noise to start with
    [Range(0, 100)]
    public int randomFillPercent = 50;

    // Heightmap stuff
    int[,] map;
    int[,] mapx2;
    float[,] heights;
    float[,] heights2;

    void Awake()
    {
        terrain = GetComponent<Terrain>();
    }

    // Kicks off the base map creation
    public void StartGeneration()
    {
        terrain = GetComponent<Terrain>();

        int mapHeight = terrain.terrainData.heightmapResolution;
        int mapWidth = terrain.terrainData.heightmapResolution;

        //Let's figure out how many times we need to shrink/grow our map
        //for the cellular automata to work. 64*64 seems to be the best resolution.
        scaleHeightMultip = (int)(mapHeight / 64);
        scaleWidthMultip = (int)(mapWidth / 64);

        height = mapHeight / scaleHeightMultip;
        width = mapWidth / scaleWidthMultip;

        heights = new float[terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution];
        heights2 = terrain.terrainData.GetHeights(0, 0, terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);

        GenerateMap();
    }

    // This runs in a thread because shore calculation takes ages and we don't want Unity to freeze up.
    void Thread1()
    {
        for (int x = 0; x < scaledWidth; x++)
        {
            for (int y = 0; y < scaledHeight; y++)
            {
                // If "aborted" gets set to true from the inspector, bail out of the loops and stop the thread
                if (aborted)
                    break;
                calculatedPixels++;
                prog = (((float)calculatedPixels / (float)gridSize) * 100.0f).ToString("F0") + "%";
                if (mapx2[x, y] == 2)
                {
                    RandomDrop(x, y);
                }
            }
            if (aborted)
                break;
        }

        // Thread's done, so reset "aborted" just in case
        aborted = false;
    }

    // Drops the whole heightmap down until the lowest point is at zero
    public void ResetSeaFloor()
    {
        terrain = GetComponent<Terrain>();

        heights2 = terrain.terrainData.GetHeights(0, 0, terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);
        bool hitFloor = false;

        // Even if we hit zero somewhere, this keeps going through the whole map so everything gets lowered evenly.
        while (!hitFloor)
        {
            for (int x = 0; x < terrain.terrainData.heightmapResolution; x++)
            {
                for (int y = 0; y < terrain.terrainData.heightmapResolution; y++)
                {
                    heights2[x, y] -= 0.001f;
                    if (heights2[x, y] <= 0)
                        hitFloor = true;
                }
            }
        }

        terrain.terrainData.SetHeights(0, 0, heights2);
    }

    // Smooths out the heightmap by averaging each pixel with its neighbors
    public void BlendHeights()
    {
        terrain = GetComponent<Terrain>();

        int hMapWidth = terrain.terrainData.heightmapResolution;
        int hMapHeight = terrain.terrainData.heightmapResolution;

        heights2 = terrain.terrainData.GetHeights(0, 0, hMapWidth, hMapHeight);

        for (int x = 0; x < hMapWidth; x++)
        {
            for (int y = 0; y < hMapHeight; y++)
            {
                float pointHeight = 0.0f;
                float blendHeight = pointHeight;
                float pixelCount = 0.0f;

                for (int x2 = x-1; x2 < x + 2; x2++)
                {
                    for (int y2 = y-1; y2 < y + 2; y2++)
                    {
                        if (x2 >= 0 && y2 >= 0 && x2 < hMapWidth && y2 < hMapHeight)
                        {
                            blendHeight = blendHeight + heights2[x2, y2];
                            pixelCount++;
                        }
                    }
                }
                blendHeight = blendHeight / pixelCount;
                // Debug print if you want: print ("point: "+pointHeight+", blend: "+blendHeight);
                // If you want to only blend low spots: if(blendHeight < 0.1f) heights2[x,y] = blendHeight;
                heights2[x, y] = blendHeight;
            }
        }
        terrain.terrainData.SetHeights(0, 0, heights2);
    }

    // Adds a sine wave bump to the shores to make them look smoother.
    // Uses x and y to figure out how far from the edge, then does the wave math and applies it.
    void RandomDrop(int xCoord, int yCoord)
    {
        // Figure out the radius for the bump. More centered = bigger radius = gentler shore.
        // Don't let it go past the max allowed.
        int radius = terrainDataMapWidth;

        int xDiff = terrainDataMapWidth - xCoord;
        int yDiff = terrainDataMapHeight - yCoord;

        if (xDiff < radius)
            radius = xDiff;

        if (yDiff < radius)
            radius = yDiff;

        if (xCoord < radius)
            radius = xCoord;

        if (yCoord < radius)
            radius = yCoord;

        if (radius > maxRadius2)
            radius = maxRadius2;

        for (int x = 0; x < radius*2; x++)
        {
            for (int y = 0; y < radius*2; y++)
            {
                
                // Calculate the bump for this spot
                float px = (float)x / (float)radius/2;
                float py = (float)y / (float)radius/2;

                float cosval = Mathf.Sin(px * Mathf.PI);
                float cosval2 = Mathf.Sin(py * Mathf.PI);

                // How tall the bump is here
                float tmpHeight = (cosval/10) * cosval2;

                // Only apply the bump if it's lower than the current height and not too high
                if ((heights[(xCoord - radius) + x, (yCoord - radius) + y]) < 0.1f && (heights[(xCoord - radius) + x, (yCoord - radius) + y]) <= tmpHeight)
                {
                    heights[(xCoord - radius) + x, (yCoord - radius) + y] = tmpHeight;
                }               
            }
        }
    }

    // Adds some extra Perlin noise to the heightmap for more natural look
    public void PerlinNoise()
    {
        terrain = GetComponent<Terrain>();

        int cHeight = terrain.terrainData.heightmapResolution;
        int cWidth = terrain.terrainData.heightmapResolution;

        float[,] originalMap = terrain.terrainData.GetHeights(0, 0, cWidth, cHeight);

        float rnd = (float)(DateTime.Now.Millisecond)/1000;

        for (int x = 0; x < cWidth; x++)
        {
            for (int y = 0; y < cHeight; y++)
            {

                float px = (float)x / (float)cWidth;
                float py = (float)y / (float)cHeight;

                originalMap[x, y] += perlinHeight * Mathf.PerlinNoise((px + rnd) * perlinScale, (py + rnd) * perlinScale);
                
            }
        }

        terrain.terrainData.SetHeights(0, 0, originalMap);
    }

    // Flattens the heightmap before starting a new map
    void Flatten()
    {
        for (int i = 0; i < terrain.terrainData.heightmapResolution; i++)
        {
            for (int k = 0; k < terrain.terrainData.heightmapResolution; k++)
            {
                heights[i, k] = 0;
            }
        }

        terrain.terrainData.SetHeights(0, 0, heights);
    }

    // Scales the map up and centers it
    void ScaleMap(int thisHeight, int thisWidth, int[,] thisMap)
    {
        scaledHeight = thisHeight * 2;
        scaledWidth = thisWidth * 2;

        mapx2 = new int[scaledWidth, scaledHeight];

        for (int x = 0; x < thisWidth; x++)
        {
            for (int y = 0; y < thisHeight; y++)
            {
                if (thisMap[x, y] == 1)
                {
                    mapx2[x * 2, y * 2] = 1;
                    mapx2[x * 2 + 1, y * 2] = 1;
                    mapx2[x * 2, y * 2 + 1] = 1;
                    mapx2[x * 2 + 1, y * 2 + 1] = 1;
                }

                else
                {
                    mapx2[x * 2, y * 2] = 0;
                    mapx2[x * 2 + 1, y * 2] = 0;
                    mapx2[x * 2, y * 2 + 1] = 0;
                    mapx2[x * 2 + 1, y * 2 + 1] = 0;
                }
            }
        }

    }

    // Flip all the 0s to 1s and vice versa
    void InvertMap()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                map[x, y] = (map[x, y] == 0) ? 1 : 0;
            }
        }

    }

    // Makes the initial map using cellular automata
    // (Inspired by Sebastian Lague's YouTube tutorials)
    void GenerateMap()
    {
        // Start by flattening everything
        Flatten();

        map = new int[width, height];

        // Fill the map with random noise
        RandomFillMap();

        // Run the cellular automata a few times
        for (int i = 0; i < smoothTimes; i++)
        {
            SmoothMap();
        }

        // Flip the map because... well, that's just how it works here
        InvertMap();

        // Scale the map back up since we shrank it earlier.
        // Big grids (like 512x512) don't look great with cellular automata, so we shrink first for better islands, then scale up.
        ScaleMap(height, width, map);

        while (scaledHeight < height * scaleHeightMultip)
        {
            ScaleMap(scaledHeight, scaledWidth, mapx2);
        }

        // Find the edges for the heightmap
        FindEdges();

        // Cellular automata made our grid: 0 = ocean, 1 = island
        // So we bump up the heightmap where there's land
        for (int x = 0; x < scaledWidth; x++)
        {
            for (int y = 0; y < scaledHeight; y++)
            {
                if (mapx2[x, y] == 1)
                    heights[x, y] = 0.1f;
                if (mapx2[x, y] == 0)
                    heights[x, y] = 0.0f;
            }
        }

        terrain.terrainData.SetHeights(0, 0, heights);
        heights2 = terrain.terrainData.GetHeights(0, 0, terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);

    }

    // Starts a thread to crunch the shore calculations
    public void CalculateShores()
    {
        terrain = GetComponent<Terrain>();

        terrainreso = terrain.terrainData.heightmapResolution;

        // Depending on resolution, we tweak the max radius for shore steepness.
        // Radius is tuned for 513 resolution, so we scale it up or down as needed.
        maxRadius2 = (int)(maxRadius * (((float)terrainreso - 1.0f) / 512.0f));

        gridSize = scaledHeight * scaledWidth;
        calculatedPixels = 0;

        terrainDataMapHeight = terrain.terrainData.heightmapResolution;
        terrainDataMapWidth = terrain.terrainData.heightmapResolution;

        th1 = new Thread(Thread1);

        th1.Start();
    }

    // Call this after the thread finishes so you can apply the shores to the map
    public void SmoothShores()
    {
        terrain = GetComponent<Terrain>();
        terrain.terrainData.SetHeights(0, 0, heights);
        heights2 = terrain.terrainData.GetHeights(0, 0, terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);
    }

    // Finds all the edges between land and water in the cellular automata map
    // and marks them so we know where to do the shore calculations
    void FindEdges()
    {
        for (int x = 1; x < scaledWidth - 1; x++)
        {
            for (int y = 1; y < scaledHeight - 1; y++)
            {
                if (mapx2[x, y] == 1)
                {
                    for (int x2 = x - 1; x2 < x + 2; x2++)
                    {
                        for (int y2 = y - 1; y2 < y + 2; y2++)
                        {
                            if (mapx2[x2, y2] == 0) mapx2[x2, y2] = 2;
                        }
                    }
                }
            }
        }
    }

    // Fills the map with random noise for the cellular automata to work with
    // (Thanks again to Sebastian Lague's tutorials)
    void RandomFillMap()
    {
        if (useRandomSeed)
        {
            seed = DateTime.Now.GetHashCode().ToString();
        }

        if (seed == null)
            seed = "0";

        System.Random pseudoRandom = new System.Random(seed.GetHashCode());

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                {
                    // Always fill the edges
                    map[x, y] = 1;
                }
                else
                {
                    // Fill the rest randomly
                    map[x, y] = (pseudoRandom.Next(0, 100) < randomFillPercent) ? 1 : 0;
                }
            }
        }
    }

    // The actual cellular automata function
    // (Yep, still inspired by Sebastian Lague)
    void SmoothMap()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int neighborWallTiles = GetSurroundingWallCount(x, y);

                if (neighborWallTiles > neighboringWalls)
                    map[x, y] = 1; // More walls around? Make it a wall.
                else if (neighborWallTiles < neighboringWalls)
                    map[x, y] = 0; // Fewer walls? Make it open.

            }
        }
    }

    // Counts how many "walls" are around each pixel for the cellular automata
    // (Yep, Sebastian Lague again)
    int GetSurroundingWallCount(int gridX, int gridY)
    {
        int wallCount = 0;

        for (int neighborX = gridX - 1; neighborX <= gridX + 1; neighborX ++)
        {
            for (int neighborY = gridY - 1; neighborY <= gridY + 1; neighborY ++)
            {
                if (neighborX >= 0 && neighborX < width && neighborY >= 0 && neighborY < height)
                {
                    if (neighborX != gridX || neighborY != gridY)
                    {
                        wallCount += map[neighborX,neighborY];
                    }
                }

                else
                {
                    wallCount ++;
                }
            }
        }

        return wallCount;
    }
}
