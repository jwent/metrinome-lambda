using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GraphQL;
using GraphQL.Authorization;

public class TrackerController {
	public static TrackingCampaign GetCampaignById(OnTrackDBContext onTrackDBContext, Guid userId, Guid id) {
		Console.WriteLine($"[+] searching campaigns by campaignId: ${id}");
		var existingCampaign = onTrackDBContext.TrackingCampaigns.FirstOrDefault(e => e.Id == id && e.ParentTracker.Organization.OwnerId == userId);
		if (existingCampaign == null)
			throw new Exception("campaign not found!");
		return existingCampaign;
	}

	public static UserTracker GetUserTrackerByUser(OnTrackDBContext onTrackDBContext, Guid userId) {
		var user = onTrackDBContext.Users
			.Include(u => u.Organization)
			.First(u => u.Id == userId);
		return onTrackDBContext.UserTrackers.First(t => t.Organization.Id == user.Organization.Id);
	}
}



