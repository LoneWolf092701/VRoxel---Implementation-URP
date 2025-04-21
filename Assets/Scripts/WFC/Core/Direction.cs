namespace WFC.Core
{
    /// <summary>
    /// Enum representing the six possible directions in 3D space.
    /// </summary>
    public enum Direction
    {
        Up,     // Y+
        Right,  // X+
        Down,   // Y-
        Left,   // X-
        Forward, // Z+
        Back     // Z-
    }

    public static class DirectionExtensions
    {
        /// <summary>
        /// Gets the opposite direction of the given direction.
        /// </summary>
        public static Direction GetOpposite(this Direction direction) => direction switch
        {
            Direction.Up => Direction.Down,
            Direction.Right => Direction.Left,
            Direction.Down => Direction.Up,
            Direction.Left => Direction.Right,
            Direction.Forward => Direction.Back,
            Direction.Back => Direction.Forward,
            _ => throw new System.ArgumentOutOfRangeException(nameof(direction))
        };
        /// <summary>
        /// Gets the Unity Vector3 representation of the given direction.
        /// </summary>
        public static UnityEngine.Vector3Int ToVector3Int(this Direction direction) => direction switch
        {
            Direction.Up => UnityEngine.Vector3Int.up,
            Direction.Right => UnityEngine.Vector3Int.right,
            Direction.Down => UnityEngine.Vector3Int.down,
            Direction.Left => UnityEngine.Vector3Int.left,
            Direction.Forward => UnityEngine.Vector3Int.forward,
            Direction.Back => UnityEngine.Vector3Int.back,
            _ => UnityEngine.Vector3Int.zero
        };
    }
}