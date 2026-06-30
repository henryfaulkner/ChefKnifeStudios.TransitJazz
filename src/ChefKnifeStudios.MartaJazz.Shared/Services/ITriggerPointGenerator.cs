using ChefKnifeStudios.MartaJazz.Shared.Models;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Shared.Services;

public interface ITriggerPointGenerator
{
    IReadOnlyList<TriggerPoint> Generate(double[][] coords, double[] cumDist);
}
