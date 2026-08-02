using ChefKnifeStudios.TransitJazz.Shared;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Shared.Tests;

// Feature 051 / US4 (P3-U5): HubMethods.JoinCity is the VERSION GATE for the v2 wire revision.
//
// MessagePack's ReadDouble accepts int encodings, so a stale client reading v2 coordinates as
// v1 doubles would NOT fail — it would silently misrender every vehicle. Renaming the join
// method is what turns that silent corruption into a clean HubException at join time. These
// tests freeze the constant so the gate cannot be reverted or renamed by accident.
public class HubMethodsTests
{
    [Fact]
    public void HubMethods_JoinCity_IsJoinCityV2()
    {
        Assert.Equal("JoinCityV2", HubMethods.JoinCity);
    }

    [Fact]
    public void HubMethods_LeaveCity_IsUnversioned()
    {
        // Contract wire-slimming C5: LeaveCity is deliberately NOT renamed alongside JoinCity.
        // Group membership only matters post-join, so a stale peer can never reach it. A blanket
        // "V2" rename would satisfy the JoinCity assertion above while silently breaking the
        // Phase 2 (US3) hidden-tab pause path, which invokes LeaveCity by this exact name.
        Assert.Equal("LeaveCity", HubMethods.LeaveCity);
    }
}
