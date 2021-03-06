﻿
using System;
using System.Collections.Generic;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using ScrewTurn.Wiki.PluginFramework;
using ScrewTurn.Wiki.AclEngine;

namespace ScrewTurn.Wiki {

	public partial class AdminGroups : BasePage {

		string currentWiki;

		protected void Page_Load(object sender, EventArgs e) {
			AdminMaster.RedirectToLoginIfNeeded();

			currentWiki = DetectWiki();

			string currentUser = SessionFacade.GetCurrentUsername();
			string[] currentGroups = SessionFacade.GetCurrentGroupNames(currentWiki);

			if(!AdminMaster.CanManageGroups(currentUser, currentGroups)) UrlTools.Redirect("AccessDenied.aspx");
			aclActionsSelector.Visible = AdminMaster.CanManagePermissions(currentUser, currentGroups);

			revName.ValidationExpression = GlobalSettings.UsernameRegex;

			if(!Page.IsPostBack) {
				rptGroups.DataBind();
				providerSelector.Reload();
				btnNewGroup.Enabled = providerSelector.HasProviders;
			}
		}

		protected void rptGroups_DataBinding(object sender, EventArgs e) {
			List<UserGroup> allGroups = Users.GetUserGroups(currentWiki);

			List<UserGroupRow> result = new List<UserGroupRow>(allGroups.Count);

			foreach(UserGroup group in allGroups) {
				result.Add(new UserGroupRow(group, group.Name == txtCurrentName.Value));
			}

			rptGroups.DataSource = result;
		}

		protected void rptGroups_ItemCommand(object sender, RepeaterCommandEventArgs e) {
			if(e.CommandName == "Select") {
				txtCurrentName.Value = e.CommandArgument as string;
				//rptGroups.DataBind(); Not needed because the list is hidden on select

				UserGroup group = Users.FindUserGroup(currentWiki, txtCurrentName.Value);

				txtName.Text = group.Name;
				txtName.Enabled = false;
				txtDescription.Text = group.Description;
				providerSelector.SelectedProvider = group.Provider.GetType().FullName;
				providerSelector.Enabled = false;

				// Select group's global permissions
				AuthReader authReader = new AuthReader(Collectors.CollectorsBox.GetSettingsProvider(currentWiki));
				aclActionsSelector.GrantedActions = authReader.RetrieveGrantsForGlobals(group);
				aclActionsSelector.DeniedActions = authReader.RetrieveDenialsForGlobals(group);

				btnCreate.Visible = false;
				btnSave.Visible = true;
				btnDelete.Visible = true;
				bool isDefaultGroup =
					group.Name == Settings.GetAdministratorsGroup(currentWiki) ||
					group.Name == Settings.GetUsersGroup(currentWiki) ||
					group.Name == Settings.GetAnonymousGroup(currentWiki);
				
				pnlEditGroup.Visible = true;
				pnlList.Visible = false;

				// Enable/disable interface sections based on provider read-only settings
				pnlGroupDetails.Enabled = !group.Provider.UserGroupsReadOnly;
				btnDelete.Enabled = !group.Provider.UserGroupsReadOnly && !isDefaultGroup;

				lblResult.CssClass = "";
				lblResult.Text = "";
			}
		}

		/// <summary>
		/// Resets the group editor's status.
		/// </summary>
		private void ResetEditor() {
			txtName.Text = "";
			txtName.Enabled = true;
			txtDescription.Text = "";
			providerSelector.Enabled = true;
			providerSelector.Reload();
			pnlGroupDetails.Enabled = true;

			aclActionsSelector.GrantedActions = new string[0];
			aclActionsSelector.DeniedActions = new string[0];

			btnCreate.Visible = true;
			btnSave.Visible = false;
			btnDelete.Visible = false;
			lblResult.Text = "";
		}

		/// <summary>
		/// Refreshes the group list.
		/// </summary>
		private void RefreshList() {
			txtCurrentName.Value = "";
			ResetEditor();
			rptGroups.DataBind();
		}

		protected void cvName_ServerValidate(object sender, ServerValidateEventArgs e) {
			if(txtName.Enabled) e.IsValid = Users.FindUserGroup(currentWiki, txtName.Text) == null;
			else e.IsValid = true;
		}

		protected void btnNewGroup_Click(object sender, EventArgs e) {
			pnlList.Visible = false;
			pnlEditGroup.Visible = true;

			lblResult.Text = "";
			lblResult.CssClass = "";
		}

		protected void btnCreate_Click(object sender, EventArgs e) {
			if(!Page.IsValid) return;

			txtName.Text = txtName.Text.Trim();

			lblResult.CssClass = "";
			lblResult.Text = "";

			Log.LogEntry("Group creation requested for " + txtName.Text, EntryType.General, SessionFacade.CurrentUsername, currentWiki);

			// Add the new group then set its global permissions
			bool done = Users.AddUserGroup(currentWiki, txtName.Text, txtDescription.Text,
				Collectors.CollectorsBox.UsersProviderCollector.GetProvider(providerSelector.SelectedProvider, currentWiki));

			UserGroup currentGroup = null;
			if(done) {
				currentGroup = Users.FindUserGroup(currentWiki, txtName.Text);
				done = AddAclEntries(currentGroup, aclActionsSelector.GrantedActions, aclActionsSelector.DeniedActions);

				if(done) {
					RefreshList();
					lblResult.CssClass = "resultok";
					lblResult.Text = Properties.Messages.GroupCreated;
					ReturnToList();
				}
				else {
					lblResult.CssClass = "resulterror";
					lblResult.Text = Properties.Messages.GroupCreatedCouldNotStorePermissions;
				}
			}
			else {
				lblResult.CssClass = "resulterror";
				lblResult.Text = Properties.Messages.CouldNotCreateGroup;
			}
		}

		protected void btnSave_Click(object sender, EventArgs e) {
			if(!Page.IsValid) return;

			lblResult.CssClass = "";
			lblResult.Text = "";

			Log.LogEntry("Group update requested for " + txtCurrentName.Value, EntryType.General, SessionFacade.CurrentUsername, currentWiki);

			UserGroup currentGroup = Users.FindUserGroup(currentWiki, txtCurrentName.Value);

			// Perform proper actions based on provider read-only settings
			// 1. If possible, modify group
			// 2. Update ACLs

			bool done = true;

			if(!currentGroup.Provider.UserGroupsReadOnly) {
				done = Users.ModifyUserGroup(currentGroup, txtDescription.Text);
			}

			if(!done) {
				lblResult.CssClass = "resulterror";
				lblResult.Text = Properties.Messages.CouldNotUpdateGroup;
				return;
			}

			done = RemoveAllAclEntries(currentGroup);
			if(done) {
				done = AddAclEntries(currentGroup, aclActionsSelector.GrantedActions, aclActionsSelector.DeniedActions);

				if(done) {
					RefreshList();
					lblResult.CssClass = "resultok";
					lblResult.Text = Properties.Messages.GroupUpdated;
					ReturnToList();
				}
				else {
					lblResult.CssClass = "resulterror";
					lblResult.Text = Properties.Messages.GroupSavedCouldNotStoreNewPermissions;
				}
			}
			else {
				lblResult.CssClass = "resulterror";
				lblResult.Text = Properties.Messages.GroupSavedCouldNotDeleteOldPermissions;
			}
		}

		protected void btnDelete_Click(object sender, EventArgs e) {
			lblResult.Text = "";
			lblResult.CssClass = "";

			Log.LogEntry("Group deletion requested for " + txtCurrentName.Value, EntryType.General, SessionFacade.CurrentUsername, currentWiki);

			UserGroup currentGroup = Users.FindUserGroup(currentWiki, txtCurrentName.Value);

			if(currentGroup.Provider.UserGroupsReadOnly) return;

			// Remove all global permissions for the group then delete it
			bool done = RemoveAllAclEntries(currentGroup);
			if(done) {
				done = Users.RemoveUserGroup(currentWiki, currentGroup);

				if(done) {
					RefreshList();
					lblResult.Text = Properties.Messages.GroupDeleted;
					lblResult.CssClass = "resultok";
					ReturnToList();
				}
				else {
					lblResult.CssClass = "resulterror";
					lblResult.Text = Properties.Messages.PermissionsDeletedCouldNotDeleteGroup;
				}
			}
			else {
				lblResult.CssClass = "resulterror";
				lblResult.Text = Properties.Messages.CouldNotDeletePermissions;
			}
		}

		protected void btnCancel_Click(object sender, EventArgs e) {
			RefreshList();
			ReturnToList();
		}

		/// <summary>
		/// Removes all the ACL entries for a group.
		/// </summary>
		/// <param name="group">The group.</param>
		/// <returns><c>true</c> if the operation succeeded, <c>false</c> otherwise.</returns>
		private bool RemoveAllAclEntries(UserGroup group) {
			AuthWriter authWriter = new AuthWriter(Collectors.CollectorsBox.GetSettingsProvider(currentWiki));
			return authWriter.RemoveEntriesForGlobals(group);
		}

		/// <summary>
		/// Adds some ACL entries for a group.
		/// </summary>
		/// <param name="group">The group.</param>
		/// <param name="grants">The granted actions.</param>
		/// <param name="denials">The denied actions.</param>
		/// <returns><c>true</c> if the operation succeeded, <c>false</c> otherwise.</returns>
		private bool AddAclEntries(UserGroup group, string[] grants, string[] denials) {
			AuthWriter authWriter = new AuthWriter(Collectors.CollectorsBox.GetSettingsProvider(currentWiki));
			foreach(string action in grants) {
				bool done = authWriter.SetPermissionForGlobals(AuthStatus.Grant, action, group);
				if(!done) return false;
			}

			foreach(string action in denials) {
				bool done = authWriter.SetPermissionForGlobals(AuthStatus.Deny, action, group);
				if(!done) return false;
			}

			return true;
		}

		/// <summary>
		/// Returns to the group list.
		/// </summary>
		private void ReturnToList() {
			pnlEditGroup.Visible = false;
			pnlList.Visible = true;
		}

	}

	/// <summary>
	/// Represents a User Group for display purposes.
	/// </summary>
	public class UserGroupRow {

		private string name, description, provider, additionalClass;
		private int users;

		/// <summary>
		/// Initializes a new instance of the <see cref="T:UserGroupRow" /> class.
		/// </summary>
		/// <param name="group">The original group.</param>
		/// <param name="selected">A value indicating whether the user group is selected.</param>
		public UserGroupRow(UserGroup group, bool selected) {
			name = group.Name;
			description = group.Description;
			provider = group.Provider.Information.Name;
			additionalClass = selected ? " selected" : "";
			users = group.Users.Length;
		}

		/// <summary>
		/// Gets the name.
		/// </summary>
		public string Name {
			get { return name; }
		}

		/// <summary>
		/// Gets the description.
		/// </summary>
		public string Description {
			get { return description; }
		}

		/// <summary>
		/// Gets the provider.
		/// </summary>
		public string Provider {
			get { return provider; }
		}

		/// <summary>
		/// Gets the additional CSS class.
		/// </summary>
		public string AdditionalClass {
			get { return additionalClass; }
		}

		/// <summary>
		/// Gets the users.
		/// </summary>
		public int Users {
			get { return users; }
		}

	}

}
