namespace GeoDataPlugin.Models
{
    // Elevation grid data
    public class ElevationGrid
    {
        public double[,] Elevations { get; set; }
        public int Rows { get; set; }
        public int Cols { get; set; }
        public double CellSize { get; set; }
        public GeoBoundingBox BoundingBox { get; set; }

        public double MinElevation
        {
            get
            {
                double min = double.MaxValue;
                for (int i = 0; i < Rows; i++)
                {
                    for (int j = 0; j < Cols; j++)
                    {
                        if (Elevations[i, j] < min) min = Elevations[i, j];
                    }
                }
                return min;
            }
        }

        public double MaxElevation
        {
            get
            {
                double max = double.MinValue;
                for (int i = 0; i < Rows; i++)
                {
                    for (int j = 0; j < Cols; j++)
                    {
                        if (Elevations[i, j] > max) max = Elevations[i, j];
                    }
                }
                return max;
            }
        }
    }
}
