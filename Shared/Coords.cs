﻿using System.Collections.Generic;
using System;

namespace Sean.Shared
{
	/// <summary>
	/// Used for mobs, objects, etc. that need more specific positioning then just block position.
	/// Three floats for 3d position and additional floats for direction and pitch.
	/// </summary>
	public struct Coords
	{
		public Coords(float x, float y, float z)
		{
			Xf = x;
			Yf = y;
			Zf = z;
			_direction = 0f;
			_pitch = 0f;
		}

		public Coords(float x, float y, float z, float direction, float pitch)
		{
			Xf = x;
			Yf = y;
			Zf = z;
			_direction = direction;
			_pitch = pitch;
		}

		/// <summary>Construct from a byte array containing the Coords values.</summary>
		/// <param name="bytes">Byte array containing coords values.</param>
		/// <param name="startIndex">Index position to start at in the byte array. Needed because sometimes other data has been sent first in the same byte array.</param>
		public Coords(byte[] bytes, int startIndex)
		{
			Xf = BitConverter.ToSingle(bytes, startIndex);
			Yf = BitConverter.ToSingle(bytes, startIndex + sizeof(float));
			Zf = BitConverter.ToSingle(bytes, startIndex + sizeof(float) * 2);
			_direction = BitConverter.ToSingle(bytes, startIndex + sizeof(float) * 3);
			_pitch = BitConverter.ToSingle(bytes, startIndex + sizeof(float) * 4);
		}

		public float Xf;
		public float Yf;
		public float Zf;

		/// <summary>X coord of the corresponding block. Readonly because Xf can always be safely set instead and this prevents accidental truncating.</summary>
		/// <remarks>the block coord is simply the truncated float</remarks>
		public int Xblock
		{
			get { return (int)Xf; } //the straight cast to int is faster then Math.Floor or Math.Truncate
		}

		/// <summary>Y coord of the corresponding block. Readonly because Yf can always be safely set instead and this prevents accidental truncating.</summary>
		/// <remarks>
		/// -the block coord is simply the truncated float
		/// -byte would be enough since you can never build higher then 256 blocks, however int is useful for allowing flying very high so the coords still calculate correctly
		/// </remarks>
		public int Yblock
		{
			get { return (int)Yf; } //the straight cast to int is faster then Math.Floor or Math.Truncate
		}

		/// <summary>Z coord of the corresponding block. Readonly because Zf can always be safely set instead and this prevents accidental truncating.</summary>
		/// <remarks>the block coord is simply the truncated float</remarks>
		public int Zblock
		{
			get { return (int)Zf; } //the straight cast to int is faster then Math.Floor or Math.Truncate
		}

		private float _direction;
		/// <summary>Direction in radians.</summary>
		public float Direction
		{
			get { return _direction; }
			set
			{
				_direction = value;
				if (_direction < 0) _direction += MathHelper.TwoPi; else if (_direction > MathHelper.TwoPi) _direction -= MathHelper.TwoPi;
			}
		}

		public Facing DirectionFacing()
		{
			if (Direction < MathHelper.PiOver4 || Direction > MathHelper.PiOver4 * 7) return Facing.East;
			if (Direction > MathHelper.PiOver4 * 5) return Facing.North;
			return Direction > MathHelper.PiOver4 * 3 ? Facing.West : Facing.South;
		}

		private float _pitch;
		/// <summary>Pitch in radians.</summary>
		public float Pitch
		{
			get { return _pitch; }
			set { _pitch = Math.Max(Math.Min(value, MathHelper.PiOver2 - 0.1f), -MathHelper.PiOver2 + 0.1f); }
		}

		/// <summary>float * 5</summary>
		public const int SIZE = sizeof(float) * 5;
		
		public byte[] ToByteArray()
		{
			var bytes = new byte[SIZE];
			BitConverter.GetBytes(Xf).CopyTo(bytes, 0);
			BitConverter.GetBytes(Yf).CopyTo(bytes, sizeof(float));
			BitConverter.GetBytes(Zf).CopyTo(bytes, sizeof(float) * 2);
			BitConverter.GetBytes(Direction).CopyTo(bytes, sizeof(float) * 3);
			BitConverter.GetBytes(Pitch).CopyTo(bytes, sizeof(float) * 4);
			return bytes;
		}

		/// <summary>Get the exact distance from the supplied coords.</summary>
		public float GetDistanceExact(ref Coords coords)
		{
			return (float)Math.Sqrt(Math.Pow(Xf - coords.Xf, 2) + Math.Pow(Yf - coords.Yf, 2) + Math.Pow(Zf - coords.Zf, 2));
		}

		/// <summary>Is this coord and the compare coord within the same block. Fast check as no math is required.</summary>
		/// <remarks>Use this to prevent building on blocks a player is standing on, etc.</remarks>
		[Obsolete("Only usages moved to Position.")]
		public bool IsOnBlock(ref Coords compare)
		{
			return Xblock == compare.Xblock && Yblock == compare.Yblock && Zblock == compare.Zblock;
		}

        public Position ToPosition()
		{
			return new Position(Xblock, Yblock, Zblock);
		}

		/// <summary>Returns block coords in the format (x={0}, y={1}, z={2})</summary>
		public override string ToString()
		{
			return string.Format("(x={0}, y={1}, z={2})", Xf, Yf, Zf);
		}
	}
}
