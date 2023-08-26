using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GraphQL;
using GraphQL.Authorization;

public class UserController {
	public const string INVITE_ORGANIZATIONAL_USER_PERMISSION = "InviteOrganizationalUser";
	public const string CREATE_CAMPAIGNS_PERMISSION = "CreateCampaigns";
	public const string EDIT_CAMPAIGNS_PERMISSION = "EditCampaigns";
	public const string READ_CAMPAIGNS_PERMISSION = "ReadCampaigns";
	public const string READ_INSIGHTS_PERMISSION = "ReadInsights";

	public static Dictionary<string, List<string>> rolePolicies = new Dictionary<string, List<string>> {
		{ "Owner", new List<string> {
			INVITE_ORGANIZATIONAL_USER_PERMISSION,
			CREATE_CAMPAIGNS_PERMISSION,
			EDIT_CAMPAIGNS_PERMISSION,
			READ_CAMPAIGNS_PERMISSION,
			READ_INSIGHTS_PERMISSION,
		} },
		{ "Viewer", new List<string> {
			INVITE_ORGANIZATIONAL_USER_PERMISSION,
			CREATE_CAMPAIGNS_PERMISSION,
			EDIT_CAMPAIGNS_PERMISSION,
			READ_CAMPAIGNS_PERMISSION,
			READ_INSIGHTS_PERMISSION,
		} },
	};

	public static Guid GetCurrentUserId(IResolveFieldContext context) {
		if (context.User?.Identity is ClaimsIdentity identity) {
			var id = identity.FindFirst("id");
			if (id != null && id.Value != null)
				return Guid.Parse(id.Value);
			else
				throw new Exception("id claim missing");
		} else
			throw new Exception("id claim missing");
	}

	public static Guid GetCurrentOrganizationId(IResolveFieldContext context, OnTrackDBContext onTrackDBContext) {
		var userId = GetCurrentUserId(context);
		var organizationId = onTrackDBContext.Users
				.Where(u => u.Id == userId)
				.Select(u => u.Organization.Id)
				.First();

		// if (organizationId == null)
		// 	throw new Exception("organizationId missing");
		// else
		return organizationId;
	}

	public static UserOrganization GetCurrentOrganization(IResolveFieldContext context, OnTrackDBContext onTrackDBContext) {
		var userId = GetCurrentUserId(context);
		var organization = onTrackDBContext.Users
				.Where(u => u.Id == userId)
				.Include(u => u.Organization.SubscriptionPlan)
				.Include(u => u.Organization.Users)
				.ThenInclude(u => u.ExtraProperties)
				.Include(u => u.Organization.Users)
				.ThenInclude(u => u.UserRoles)
				.Select(u => u.Organization)
				.First();
		return organization;
	}

	public static List<string> GetUserOrganizationalRoles(OnTrackDBContext onTrackDBContext, Guid userId, Guid organizationId) {
		return onTrackDBContext.UserOrganizationalRoleAssociations
			.Where(r => r.OrganizationUser.Id == userId && r.Organization.Id == organizationId)
			.Select(r => r.RoleName)
			.ToList();
	}

	public static bool CanUserDo(OnTrackDBContext onTrackDBContext, Guid userId, Guid organizationId, string action) {
		return GetUserOrganizationalRoles(onTrackDBContext, userId, organizationId).SelectMany(role => rolePolicies[role]).Contains(action);
	}

	public static OrganizationalSubscriptionPlan GetSubscriptionPlanByKey(OnTrackDBContext onTrackDBContext, String plankey) {
		return onTrackDBContext.OrganizationalSubscriptionPlans.First(plan => plan.PlanKey == plankey);
	}

	public static OrganizationalSubscriptionPlan? GetSubscriptionPlanByFree(OnTrackDBContext onTrackDBContext) {
		return onTrackDBContext.OrganizationalSubscriptionPlans.FirstOrDefault(plan => plan.IsFreePlan);
	}

	public static string? ValidatePasswordCreation(string password) {
		if (password.Length < 12)
			return "Password too short.";
		if (!password.Any(char.IsUpper))
			return "Password must contain an uppercase letter.";
		if (!password.Any(char.IsLower))
			return "Password must contain a lowercase letter.";
		if (!password.Any(char.IsDigit))
			return "Password must contain a number.";
		return null;
	}
}


