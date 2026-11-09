/** Shape of the Gateway's GET /api/user. */
export interface UserProfile {
  isAuthenticated: boolean;
  name: string;
  subject: string;
  roles: string[];
}
