﻿using System;
using System.Collections.Generic;
using Sean.Shared;
using System.Collections.Concurrent;
using System.Linq;

namespace Sean.WorldGenerator
{
    public class WorldEventArgs : EventArgs
    {
        Position blockLocation;
        ItemAction action;
        Block block;
    }     

    public enum ChunkEventTarget
    {
        Block,
        Item,
        Character,
        Lighting,
        Projectile,
        Sound,
        Message
    }
    public enum ItemAction
    {
        Add,
        Remove,
        Update       
    }
        
    /// <summary>
    /// World environment type. Integer value is saved in world settings XML, so these integer values cannot be changed without breaking existing worlds.
    /// Start at 1 so we can ensure this gets loaded properly and not defaulted to zero.
    /// </summary>
    public enum WorldType : byte
    {
        Grass = 1,
        Winter = 2,
        Desert = 3
    }

    public static class World
    {
        public delegate void WorldEventHandler(WorldEventArgs e);
        public static event WorldEventHandler WorldEvents;

        private static Dictionary<ChunkCoords, List<int> > registrations;
        private static LocalMap localMap;
        private static WorldMap worldMap;
        //public ConcurrentDictionary<int, Mob> Mobs { get; private set; }
        public static ConcurrentDictionary<int, GameItemDynamic> GameItems { get; private set; }


        #region Properties (Saved)
        public static WorldType WorldType { get; set; }
        /// <summary>Original Raw Seed used to generate this world. Blank if no seed was used.</summary>
        public static int RawSeed { get; set; }
        /// <summary>Original program version used when this world was generated.</summary>
        public static string GeneratorVersion { get; set; }

        public static int ChunkSize { get; set; } 

        public static int GameObjectIdSeq;
        public static int NextGameObjectId
        {
            get { return System.Threading.Interlocked.Increment(ref GameObjectIdSeq); }
        }


        private static int _sizeInChunksX;
        /// <summary>Number of chunks in X direction that make up the world.</summary>
        public static int SizeInChunksX
        {
            get { return _sizeInChunksX; }
            set
            {
                _sizeInChunksX = value;
                SizeInBlocksX = _sizeInChunksX * Chunk.CHUNK_SIZE;
            }
        }

        private static int _sizeInChunksZ;
        /// <summary>Number of chunks in Z direction that make up the world.</summary>
        public static int SizeInChunksZ
        {
            get { return _sizeInChunksZ; }
            set
            {
                _sizeInChunksZ = value;
                SizeInBlocksZ = _sizeInChunksZ * Chunk.CHUNK_SIZE;
            }
        }

        /// <summary>Number of blocks in X direction that make up the world.</summary>
        public static int SizeInBlocksX { get; private set; }

        /// <summary>Number of blocks in Z direction that make up the world.</summary>
        public static int SizeInBlocksZ { get; private set; }


        public static int MaxXChunk { get { return LocalMap.MaxXChunk; } }
        public static int MinXChunk { get { return LocalMap.MinXChunk; } }
        public static int MaxZChunk { get { return LocalMap.MaxZChunk; } }
        public static int MinZChunk { get { return LocalMap.MinZChunk; } }

        #endregion

        #region Properties (Dynamic)
        /// <summary>True when the world has been completely loaded from disk for server and single player or when world has been completely received in multiplayer.</summary>
        public static bool IsLoaded { get; set; }
        //public static Chunks Chunks;
        public static bool GenerateWithTrees = true;
        #endregion



        internal static LocalMap LocalMap { get { return localMap; } }
        internal static WorldMap WorldMap { get { return worldMap; } }

        static World ()
        {
            //Mobs = new ConcurrentDictionary<int, Mob>();
            GameItems = new ConcurrentDictionary<int, GameItemDynamic>();

            RawSeed = 123456; //settingsNode.Attributes["RawSeed"].Value;
            GeneratorVersion = "1.0";// settingsNode.Attributes["GeneratorVersion"].Value;
            GameObjectIdSeq = 1; //int.Parse(settingsNode.Attributes["GameObjectIdSeq"].Value);
            WorldType = WorldType.Grass;// (WorldType)Convert.ToInt32(settingsNode.Attributes["WorldType"].Value);

            ChunkSize = 32;

            registrations = new Dictionary<ChunkCoords, List<int> > ();

            worldMap = new WorldMap(RawSeed);
            localMap = new LocalMap(RawSeed);
        }

        public static bool IsChunkLoaded(ChunkCoords chunkCoords)
        { return localMap.IsChunkLoaded(chunkCoords); }

        public static int GetChunkSize()
        {
            return Chunk.CHUNK_SIZE;
        }
        public static ChunkCoords GetChunkCoords(Position position)
        {
            return new ChunkCoords (position.X / Chunk.CHUNK_SIZE, position.Z / Chunk.CHUNK_SIZE); 
        }
        public static Chunk GetChunk(ChunkCoords chunkCoords, int id)
        {
            var chunk = localMap.Chunk(chunkCoords);
            if (!registrations.ContainsKey (chunkCoords)) {
                registrations [chunkCoords] = new List<int> ();
            }
            registrations[chunkCoords].Add(id);
            return chunk;
        }
        public static void ChunkIgnore(ChunkCoords chunkCoords, int id)
        {
            var reg = registrations [chunkCoords];
            if (reg != null)
            {
                reg.Remove (id);
                if (reg.Count == 0) {
                    registrations.Remove (chunkCoords);
                }
            }
        }

        public static Array<int> GetGlobalMap()
        {
            return worldMap.GetMap();
        }
        public static bool IsGlobalMapWater(int x, int z)
        {
            return worldMap.GetMap()[x, z] < Generator.waterLevel; // TODO - temp
        }

        public static void RenderMap()
        {
            localMap.Render();
        }


        public static void PutBlock(Position position, Block.BlockType blockType)
        {
            PlaceBlock(position, blockType);
        }



        #region Lookup Functions
        /// <summary>Get a block using world coords.</summary>
        internal static Block GetBlock(ref Coords coords)
        {
            return localMap.Chunk(coords).Blocks[coords];
        }

        /// <summary>Get a block using world x,y,z. Use this overload to avoid constructing coords when they arent needed.</summary>
        /// <remarks>For example, this provided ~40% speed increase in the World.PropagateLight function compared to constructing coords and calling the above overload.</remarks>
        internal static Block GetBlock(int x, int y, int z)
        {
            return localMap.Chunk(x / Chunk.CHUNK_SIZE, z / Chunk.CHUNK_SIZE).Blocks[x % Chunk.CHUNK_SIZE, y, z % Chunk.CHUNK_SIZE];
        }

        /// <summary>
        /// Is this position a valid block location. Includes blocks on the base of the world even though they cannot be removed.
        /// This is because the cursor can still point at them, they can still receive light, etc.
        /// Coords/Position structs have the same method. Use this one to avoid contructing coords/position when they arent needed. Large performance boost in some cases.
        /// </summary>
        internal static bool IsValidBlockLocation(int x, int y, int z)
        {
            return x >= 0 && x < SizeInBlocksX && y >= 0 && y < Chunk.CHUNK_HEIGHT && z >= 0 && z < World.SizeInBlocksZ;
        }

        internal static bool IsOnChunkBorder(int x, int z)
        {
            return x % Chunk.CHUNK_SIZE == 0 || z % Chunk.CHUNK_SIZE == 0 || x % Chunk.CHUNK_SIZE == Chunk.CHUNK_SIZE - 1 || z % Chunk.CHUNK_SIZE == Chunk.CHUNK_SIZE - 1;
        }

        internal static int GetHeightMapLevel(int x, int z)
        {
            return LocalMap.Chunk(x / Chunk.CHUNK_SIZE, z / Chunk.CHUNK_SIZE).HeightMap[x % Chunk.CHUNK_SIZE, z % Chunk.CHUNK_SIZE];
        }

        /// <summary>Check if any of 4 directly adjacent blocks receive direct sunlight. Uses the heightmap so that the server can also use this method. If the server stored light info then it could be used instead.</summary>
        internal static bool HasAdjacentBlockReceivingDirectSunlight(int x, int y, int z)
        {
            return (x < World.SizeInBlocksX - 1 && GetHeightMapLevel(x + 1, z) <= y) ||
                (x > 0 && GetHeightMapLevel(x - 1, z) <= y) ||
                (z < World.SizeInBlocksZ - 1 && GetHeightMapLevel(x, z + 1) <= y) ||
                (z > 0 && GetHeightMapLevel(x, z - 1) <= y);
        }



        internal static bool IsValidItemLocation(Position position)
        {
            return IsValidBlockLocation(position.X, 0, position.Z) && position.Y >= 0; 
        }

        internal static bool IsOnChunkBorder(Position position)
        {
            return IsOnChunkBorder(position.X, position.Z);
        }


        /// <summary>
        /// Get a List of chunks this block is bordering. Result count must be in the range 0-2 because a block can border at most 2 chunks at a time.
        /// Accounts for world edges and does not add results in those cases.
        /// </summary>
        internal static List<Chunk> BorderChunks(Position position)
        {
            var chunks = new List<Chunk>();
            //check in X direction
            if (position.X > 0 && position.X % Chunk.CHUNK_SIZE == 0)
            {
                chunks.Add(localMap.Chunk((position.X - 1) / Chunk.CHUNK_SIZE, position.Z / Chunk.CHUNK_SIZE)); //add left chunk
            }
            else if (position.X < World.SizeInBlocksX - 1 && position.X % Chunk.CHUNK_SIZE == Chunk.CHUNK_SIZE - 1)
            {
                chunks.Add(localMap.Chunk((position.X + 1) / Chunk.CHUNK_SIZE, position.Z / Chunk.CHUNK_SIZE)); //add right chunk
            }
            //check in Z direction
            if (position.Z > 0 && position.Z % Chunk.CHUNK_SIZE == 0)
            {
                chunks.Add(localMap.Chunk(position.X / Chunk.CHUNK_SIZE, (position.Z - 1) / Chunk.CHUNK_SIZE)); //add back chunk
            }
            else if (position.Z < World.SizeInBlocksZ - 1 && position.Z % Chunk.CHUNK_SIZE == Chunk.CHUNK_SIZE - 1)
            {
                chunks.Add(localMap.Chunk(position.X / Chunk.CHUNK_SIZE, (position.Z + 1) / Chunk.CHUNK_SIZE)); //add front chunk
            }
            return chunks;
        }

        internal static bool IsValidBlockLocation(Position position)
        {
            return IsValidBlockLocation(position.X, position.Y, position.Z); 
        }
        internal static bool IsValidBlockLocation(Coords coords)
        {
            return IsValidBlockLocation(coords.Xblock, coords.Yblock, coords.Zblock); 
        }

        internal static bool IsValidPlayerLocation(Coords coords)
        {
            return coords.Xf >= 0 && coords.Xf < World.SizeInBlocksX
                && coords.Yf >= 0 && coords.Yf <= 600 //can't see anything past 600
                && coords.Zf >= 0 && coords.Zf < World.SizeInBlocksZ
                && (coords.Yf >= Chunk.CHUNK_HEIGHT || !GetBlock(coords.ToPosition()).IsSolid)
                && (coords.Yf + 1 >= Chunk.CHUNK_HEIGHT || !GetBlock(coords.Xblock, coords.Yblock + 1, coords.Zblock).IsSolid)
                && (coords.Yf % 1 < Constants.PLAYER_HEADROOM || coords.Yf + 2 >= Chunk.CHUNK_HEIGHT || !GetBlock(coords.Xblock, coords.Yblock + 2, coords.Zblock).IsSolid); //the player can occupy 3 blocks
        }

        internal static bool IsValidItemLocation(Coords coords)
        {
            return IsValidBlockLocation(coords.Xblock, 0, coords.Zblock) && coords.Yf >= 0;
        }

        [Obsolete("Only usages moved to Position.")]
        internal static bool IsOnChunkBorder(Coords coords)
        {
            return IsOnChunkBorder(coords.Xblock, coords.Zblock);
        }

        /// <summary>Get a List of the 6 directly adjacent positions. Exclude positions that are outside the world or on the base of the world.</summary>
        internal static List<Position> AdjacentPositions(Position position)
        {
            var positions = new List<Position>();
            var left = new Position(position.X - 1, position.Y, position.Z);
            if (IsValidBlockLocation(left) && left.Y >= 1) positions.Add(left);
            var right = new Position(position.X + 1, position.Y, position.Z);
            if (IsValidBlockLocation(right) && right.Y >= 1) positions.Add(right);
            var front = new Position(position.X, position.Y, position.Z + 1);
            if (IsValidBlockLocation(front) && front.Y >= 1) positions.Add(front);
            var back = new Position(position.X, position.Y, position.Z - 1);
            if (IsValidBlockLocation(back) && back.Y >= 1) positions.Add(back);
            var top = new Position(position.X, position.Y + 1, position.Z);
            if (IsValidBlockLocation(top) && top.Y >= 1) positions.Add(top);
            var bottom = new Position(position.X, position.Y - 1, position.Z);
            if (IsValidBlockLocation(bottom) && bottom.Y >= 1) positions.Add(bottom);
            return positions;
        }

        /// <summary>Get a List of the 6 directly adjacent positions and corresponding faces. Exclude positions that are outside the world or on the base of the world.</summary>
        internal static List<Tuple<Position, Face>> AdjacentPositionFaces(Position position)
        {
            var positions = new List<Tuple<Position, Face>>();
            var left = new Position(position.X - 1, position.Y, position.Z);
            if (IsValidBlockLocation(left) && left.Y >= 1) positions.Add(new Tuple<Position, Face>(left, Face.Left));
            var right = new Position(position.X + 1, position.Y, position.Z);
            if (IsValidBlockLocation(right) && right.Y >= 1) positions.Add(new Tuple<Position, Face>(right, Face.Right));
            var front = new Position(position.X, position.Y, position.Z + 1);
            if (IsValidBlockLocation(front) && front.Y >= 1) positions.Add(new Tuple<Position, Face>(front, Face.Front));
            var back = new Position(position.X, position.Y, position.Z - 1);
            if (IsValidBlockLocation(back) && back.Y >= 1) positions.Add(new Tuple<Position, Face>(back, Face.Back));
            var top = new Position(position.X, position.Y + 1, position.Z);
            if (IsValidBlockLocation(top) && top.Y >= 1) positions.Add(new Tuple<Position, Face>(top, Face.Top));
            var bottom = new Position(position.X, position.Y - 1, position.Z);
            if (IsValidBlockLocation(bottom) && bottom.Y >= 1) positions.Add(new Tuple<Position, Face>(bottom, Face.Bottom));
            return positions;
        }

        /// <summary>Get a List of the 6 directly adjacent positions. Exclude positions that are outside the world or on the base of the world.</summary>
        internal static List<Position> AdjacentPositions(Coords coord)
        {
            return AdjacentPositions(coord.ToPosition());
        }

        /// <summary>
        /// Get a block using world coords. Looks up the chunk from the world chunks array and then the block in the chunk blocks array.
        /// Therefore if you have a chunk and chunk relative xyz its faster to get the block straight from the chunk blocks array.
        /// </summary>
        internal static Block GetBlock(Position position)
        {
            return localMap.Chunk(position).Blocks[position];
        }
        #endregion

        #region Block Place
        /// <summary>Place a single block in the world. This will mark the block as dirty.</summary>
        /// <param name="position">position to place the block at</param>
        /// <param name="type">type of block to place</param>
        /// <param name="isMultipleBlockPlacement">Use this when placing multiple blocks at once so lighting and chunk queueing only happens once.</param>
        internal static void PlaceBlock(Position position, Block.BlockType type, bool isMultipleBlockPlacement = false)
        {
            if (!IsValidBlockLocation(position) || position.Y <= 0) return;

            //this was a multiple block placement, prevent placing blocks on yourself and getting stuck; used to be able to place cuboids on yourself and get stuck
            //only check in single player for now because in multiplayer this could allow the blocks on different clients to get out of sync and placements of multiple blocks in multiplayer will be rare
            //if (Config.IsSinglePlayer && isMultipleBlockPlacement && (position.IsOnBlock(ref Game.Player.Coords) || position == Game.Player.CoordsHead.ToPosition())) return;

            if (type == Block.BlockType.Air)
            {
                //if destroying a block under water, fill with water instead of air
                if (position.Y + 1 < Chunk.CHUNK_HEIGHT && GetBlock(position.X, position.Y + 1, position.Z).Type == Block.BlockType.Water) type = Block.BlockType.Water;
            }

            var chunk = localMap.Chunk(position);
            var block = GetBlock(position);
            var oldType = block.Type;
            block.Type = type; //assign the new type
            var isTransparentBlock = Block.IsBlockTypeTransparent(type);
            var isTransparentOldBlock = Block.IsBlockTypeTransparent(oldType);
            block.BlockData = (ushort)(block.BlockData | 0x8000); //mark the block as dirty for the save file "diff" logic
            chunk.Blocks[position] = block; //insert the new block
            chunk.UpdateHeightMap(ref block, position.X % Chunk.CHUNK_SIZE, position.Y, position.Z % Chunk.CHUNK_SIZE);

            if (!isTransparentBlock || type == Block.BlockType.Water)
            {
                var below = position;
                below.Y--;
                if (below.Y > 0)
                {
                    if (GetBlock(below).Type == Block.BlockType.Grass || GetBlock(below).Type == Block.BlockType.Snow)
                    {
                        PlaceBlock(below, Block.BlockType.Dirt, true); //dont queue with this dirt block change, the source block changing takes care of it, prevents double queueing the chunk and playing sound twice
                    }
                }
            }

            if (!chunk.WaterExpanding) //water is not expanding, check if this change should make it start
            {
                switch (type)
                {
                case Block.BlockType.Water:
                    chunk.WaterExpanding = true;
                    break;
                case Block.BlockType.Air:
                    for (var q = 0; q < 5; q++)
                    {
                        Position adjacent;
                        switch (q)
                        {
                        case 0:
                            adjacent = new Position(position.X + 1, position.Y, position.Z);
                            break;
                        case 1:
                            adjacent = new Position(position.X - 1, position.Y, position.Z);
                            break;
                        case 2:
                            adjacent = new Position(position.X, position.Y + 1, position.Z);
                            break;
                        case 3:
                            adjacent = new Position(position.X, position.Y, position.Z + 1);
                            break;
                        default:
                            adjacent = new Position(position.X, position.Y, position.Z - 1);
                            break;
                        }
                        if (IsValidBlockLocation(adjacent) && GetBlock(adjacent).Type == Block.BlockType.Water)
                        {
                            localMap.Chunk(adjacent).WaterExpanding = true;
                        }
                    }
                    break;
                }
            }

            //its easier to just set .GrassGrowing on all affected chunks to true, and then let that logic figure it out and turn it off, this way the logic is contained in one spot
            //and also the logic here doesnt need to run every time a block gets placed. ie: someone is building a house, its running through this logic for every block placement;
            //now it can only check once on the grass grow interval and turn it back off
            //gm: an additional optimization, grass could never start growing unless this is an air block and its replacing a non transparent block
            //OR this is a non transparent block filling in a previously transparent block to cause grass to die
            if (!isTransparentBlock || (type == Block.BlockType.Air && !isTransparentOldBlock))
            {
                chunk.GrassGrowing = true;
                if (IsOnChunkBorder(position))
                {
                    foreach (var adjacentChunk in BorderChunks(position)) adjacentChunk.GrassGrowing = true;
                }
            }

            //determine if any static game items need to be removed as a result of this block placement
            if (type != Block.BlockType.Air)
            {
                //lock (chunk.Clutters) //lock because clutter is stored in a HashSet
                //{
                //  //if theres clutter on this block then destroy it to place the block (FirstOrDefault returns null if no match is found)
                //  var clutterToRemove = chunk.Clutters.FirstOrDefault(clutter => position.IsOnBlock(ref clutter.Coords));
                //  if (clutterToRemove != null) chunk.Clutters.Remove(clutterToRemove);
                //}

                var lightSourceToRemove = chunk.LightSources.FirstOrDefault(lightSource => position.IsOnBlock(ref lightSource.Value.Coords));
                if (lightSourceToRemove.Value != null)
                {
                    LightSource temp;
                    chunk.LightSources.TryRemove(lightSourceToRemove.Key, out temp);
                }
            }
            else //destroying block
            {
                /*
                lock (chunk.Clutters) //lock because clutter is stored in a HashSet
                {
                    //if theres clutter on top of this block then remove it as well (FirstOrDefault returns null if no match is found)
                    var clutterToRemove = chunk.Clutters.FirstOrDefault(clutter => clutter.Coords.Xblock == position.X && clutter.Coords.Yblock == position.Y + 1 && clutter.Coords.Zblock == position.Z); //add one to Y to look on the block above
                    if (clutterToRemove != null) chunk.Clutters.Remove(clutterToRemove);
                }
                */            

                //look on ALL 6 adjacent blocks for static items, and those only get destroyed if its on the matching opposite attached to face
                var adjacentPositions = AdjacentPositionFaces(position);
                foreach (var tuple in adjacentPositions)
                {
                    var adjBlock = GetBlock(tuple.Item1);
                    if (adjBlock.Type != Block.BlockType.Air) continue; //position cannot contain an item if the block is not air
                    var adjChunk = tuple.Item2 == Face.Top || tuple.Item2 == Face.Bottom ? chunk : LocalMap.Chunk(tuple.Item1); //get the chunk in case the adjacent position crosses a chunk boundary
                    var lightSourceToRemove = adjChunk.LightSources.FirstOrDefault(lightSource => tuple.Item1.IsOnBlock(ref lightSource.Value.Coords));
                    if (lightSourceToRemove.Value != null && lightSourceToRemove.Value.AttachedToFace == tuple.Item2.ToOpposite()) //remove the light source
                    {
                        LightSource temp;
                        chunk.LightSources.TryRemove(lightSourceToRemove.Key, out temp);
                    }
                }

                //if theres a dynamic item on top of this block then let it fall
                foreach (var item in chunk.GameItems.Values)
                {
                    if (!item.IsMoving && item.Coords.Xblock == position.X && item.Coords.Yblock == position.Y + 1 && item.Coords.Zblock == position.Z)
                    {
                        item.IsMoving = true;
                    }
                }
            }
        }

        /// <summary>Place multiple blocks in the world of the same type.</summary>
        /// <param name="startPosition">start placing blocks at</param>
        /// <param name="endPosition">stop placing blocks at</param>
        /// <param name="type">type of block to place</param>
        /// <param name="isMultipleCuboidPlacement">Use this when placing multiple cuboids at once so lighting and chunk queueing only happens once.</param>
        internal static void PlaceCuboid(Position startPosition, Position endPosition, Block.BlockType type, bool isMultipleCuboidPlacement = false)
        {
            for (var x = Math.Min(startPosition.X, endPosition.X); x <= Math.Max(startPosition.X, endPosition.X); x++)
            {
                for (var y = Math.Min(startPosition.Y, endPosition.Y); y <= Math.Max(startPosition.Y, endPosition.Y); y++)
                {
                    for (var z = Math.Min(startPosition.Z, endPosition.Z); z <= Math.Max(startPosition.Z, endPosition.Z); z++)
                    {
                        PlaceBlock(new Position(x, y, z), type, true);
                    }
                }
            }
        }
        #endregion

        #region Disk
        /// <summary>
        /// Save the world to disk. Let the caller decide if this should be in a thread because in some situations it shouldnt (ie: when loading a newly generated world the file has to be saved first).
        /// This is only called by a standalone server or a server thread running in single player. In single player the user can also manually initiate a save in which case this will be called using a Task.
        /// </summary>
        internal static void SaveToDisk()
        {
            /*
            if (File.Exists(Settings.WorldFileTempPath)) File.Delete(Settings.WorldFileTempPath);

            var fstream = new FileStream(Settings.WorldFileTempPath, FileMode.Create);
            var gzstream = new GZipStream(fstream, CompressionMode.Compress);
            //GZipStream only applies compression during .Write, writing 2 bytes at a time ends up inflating it a lot. Adding this saves up to 99.3%
            var buffstream = new BufferedStream(gzstream, 65536);
            var chunkBytes = new byte[Chunk.SIZE_IN_BYTES];

            var worldSettings = WorldSettings.GetXmlByteArray();
            buffstream.Write(BitConverter.GetBytes(worldSettings.Length), 0, sizeof(int));
            buffstream.Write(worldSettings, 0, worldSettings.Length); //write the length of the world config xml

            for (var x = 0; x < SizeInChunksX; x++)
            {
                for (var z = 0; z < SizeInChunksZ; z++)
                {
                    Buffer.BlockCopy(Chunks[x,z].Blocks.Array, 0, chunkBytes, 0, chunkBytes.Length);
                    //Buffer.BlockCopy(Chunks[x,z].Blocks.DiffArray, 0, chunkBytes, 0, chunkBytes.Length); 'bm: this will save a diff instead, WIP
                    buffstream.Write(chunkBytes, 0, chunkBytes.Length);
                }
            }
            buffstream.Flush();
            buffstream.Close();
            gzstream.Close();
            fstream.Close();
            buffstream.Dispose();
            gzstream.Dispose();
            fstream.Dispose();

            File.Copy(Settings.WorldFileTempPath, Settings.WorldFilePath, true);
            File.Delete(Settings.WorldFileTempPath);
*/
        }

        /// <summary>
        /// Called from Server.Controller class only. The scenarios where we load from disk are if this is a server launching with a previously saved world
        /// or if this is a single player and the server thread is loading the previously saved world.
        /// </summary>
        internal static void LoadFromDisk()
        {
            /*
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var fstream = new FileStream(Settings.WorldFilePath, FileMode.Open);
            var gzstream = new GZipStream(fstream, CompressionMode.Decompress);

            var bytesRead = 0;
            var worldSettingsSizeBytes = new byte[sizeof(int)];
            while (bytesRead < sizeof(int))
            {
                bytesRead += gzstream.Read(worldSettingsSizeBytes, bytesRead, sizeof(int) - bytesRead); //read the size of the world config xml
            }
            var worldSettingsBytes = new byte[BitConverter.ToInt32(worldSettingsSizeBytes, 0)];

            bytesRead = 0;
            while (bytesRead < worldSettingsBytes.Length)
            {
                bytesRead += gzstream.Read(worldSettingsBytes, bytesRead, worldSettingsBytes.Length - bytesRead);
            }
            // TODO . load settings
            //WorldSettings.LoadSettings(worldSettingsBytes);

            var chunkTotal = SizeInChunksX * SizeInChunksZ;
            var chunkCount = 1;
            var tasks = new Task[chunkTotal];
            for (var x = 0; x < SizeInChunksX; x++) //loop through each chunk and load it
            {
                for (var z = 0; z < SizeInChunksZ; z++)
                {
                    var chunkBytes = new byte[Chunk.SIZE_IN_BYTES];
                    bytesRead = 0;
                    while (bytesRead < chunkBytes.Length)
                    {
                        bytesRead += gzstream.Read(chunkBytes, bytesRead, chunkBytes.Length - bytesRead);
                    }
                    int x1 = x, z1 = z;
                    var task = Task.Factory.StartNew(() => LoadChunk(Chunks[x1, z1], chunkBytes));
                    tasks[chunkCount - 1] = task;
                    chunkCount++;
                }
            }
            Task.WaitAll(tasks);
            gzstream.Close();
            fstream.Close();

            stopwatch.Stop();
            Debug.WriteLine("World load from disk time: {0}ms", stopwatch.ElapsedMilliseconds);

            //InitializeAllLightMaps();
*/         
        }

        internal static void LoadChunk(Chunk chunk, byte[] bytes)
        {
            Buffer.BlockCopy(bytes, 0, chunk.Blocks.Array, 0, bytes.Length);
            chunk.BuildHeightMap();
        }
        #endregion

        #region Lighting
        /// <summary>Sky lightmap of the entire world.</summary>
        /// <remarks>
        /// -could become a circular array down the road if we want even bigger worlds.
        /// -could also hold both sky light and item light by using bit operations, both values 0-15 can fit in one byte
        /// </remarks>
        internal static byte[, ,] SkyLightMap;

        /// <summary>Item lightmap of the entire world. Stored separately because item light is not affected by the sky.</summary>
        internal static byte[, ,] ItemLightMap;

        #endregion
    }
}

