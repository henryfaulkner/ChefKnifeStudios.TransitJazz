using ChefKnifeStudios.TransitJazz.Shared.Models;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Shared.Services;

public interface ITriggerPointGenerator
{
    IReadOnlyList<TriggerPoint> Generate(double[][] coords, double[] cumDist);
}
