export const permissionAreas: Record<string, string> = {
  announcements: "Announcements", applications: "Applications", audit: "Audit log",
  checkin: "Check-in", email: "Email campaigns", events: "Events", forms: "Forms",
  judging: "Judging", people: "People", sponsors: "Sponsorship", swag: "Swag",
};

const labels: Record<string, string> = {
  "announcements.post": "Post announcements",
  "applications.bulk_decide": "Make bulk application decisions",
  "applications.decide": "Make application decisions",
  "applications.export": "Export applicant data",
  "applications.note": "Add applicant notes",
  "applications.view": "View applications",
  "applications.view_responses": "View application responses",
  "applications.view_resume": "View applicant resumes",
  "audit.view": "View the audit log",
  "checkin.override": "Override check-in failures",
  "checkin.scan": "Check in attendees",
  "checkin.view_stats": "View check-in statistics",
  "email.delete_templates": "Delete email templates",
  "email.manage_templates": "Manage email templates",
  "email.send_broadcast": "Send email campaigns",
  "email.send_templated": "Send individual template emails",
  "email.view_stats": "View email performance",
  "events.manage": "Manage events",
  "forms.manage": "Manage forms",
  "judging.assign": "Assign judges",
  "judging.score_assigned": "Score assigned projects",
  "judging.view_all": "View all judging results",
  "people.grant_permissions": "Grant individual permissions",
  "people.manage_teams": "Manage teams and account access",
  "people.view": "View people",
  "sponsors.edit": "Edit sponsors",
  "sponsors.view": "View sponsors",
  "sponsors.view_financials": "View sponsor financials",
  "swag.scan": "Record swag collection",
};

export function permissionLabel(permission: string) {
  return labels[permission] ?? permission.replaceAll(/[._]/g, " ").replace(/^./, letter => letter.toUpperCase());
}
