import { QueryClient } from '@tanstack/react-query';
import { UserManager, WebStorageStateStore } from 'oidc-client-ts';

export const userManager = new UserManager({
  authority: "http://localhost:8080/realms/GH-Test-Realm",
  client_id: "test-public-client",
  redirect_uri: window.location.origin,
  post_logout_redirect_uri: window.location.origin,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  monitorSession: true, // this allows cross tab login/logout detection
  response_type: 'code',
  scope: 'openid profile email',
  automaticSilentRenew: true,
  silent_redirect_uri: window.location.origin,
  loadUserInfo: true,
  metadata: {
    authorization_endpoint: "http://localhost:8080/realms/GH-Test-Realm/protocol/openid-connect/auth",
    token_endpoint: "http://localhost:8080/realms/GH-Test-Realm/protocol/openid-connect/token",
    end_session_endpoint: "http://localhost:8080/realms/GH-Test-Realm/protocol/openid-connect/logout",
    userinfo_endpoint: "http://localhost:8080/realms/GH-Test-Realm/protocol/openid-connect/userinfo",
    jwks_uri: "http://localhost:8080/realms/GH-Test-Realm/protocol/openid-connect/certs"
  },
  // Additional settings that might help
  filterProtocolClaims: false,
  accessTokenExpiringNotificationTimeInSeconds: 60
});

// Add event listeners for debugging
userManager.events.addUserLoaded((user) => {
  console.log('User loaded:', user);
});

userManager.events.addUserUnloaded(() => {
  console.log('User unloaded');
});

userManager.events.addAccessTokenExpiring(() => {
  console.log('Access token expiring');
});

userManager.events.addAccessTokenExpired(() => {
  console.log('Access token expired');
});

userManager.events.addSilentRenewError((error) => {
  console.error('Silent renew error:', error);
});

userManager.events.addUserSignedOut(() => {
  console.log('User signed out');
});

// Add more debugging events
userManager.events.addSilentRenewError((error) => {
  console.error('Silent renew error:', error);
});

userManager.events.addUserSignedOut(() => {
  console.log('User signed out');
});

// Add request/response debugging
userManager.events.addAccessTokenExpiring(() => {
  console.log('Access token expiring');
});

userManager.events.addAccessTokenExpired(() => {
  console.log('Access token expired');
});

// Add more comprehensive debugging
userManager.events.addUserLoaded((user) => {
  console.log('=== USER LOADED ===');
  console.log('User:', user);
  console.log('Access token:', user.access_token ? 'Present' : 'None');
  console.log('ID token:', user.id_token ? 'Present' : 'None');
  console.log('Expires at:', user.expires_at);
  console.log('==================');
});

userManager.events.addSilentRenewError((error) => {
  console.error('=== SILENT RENEW ERROR ===');
  console.error('Error:', error);
  console.error('=======================');
});

userManager.events.addUserSignedOut(() => {
  console.log('=== USER SIGNED OUT ===');
  console.log('======================');
});

// Add comprehensive error logging
userManager.events.addSilentRenewError((error) => {
  console.error('=== SILENT RENEW ERROR ===');
  console.error('Error:', error);
  console.error('Error details:', {
    name: error.name,
    message: error.message,
    stack: error.stack
  });
  console.error('=======================');
});

// Add user manager error logging
userManager.events.addSilentRenewError((error) => {
  console.error('=== USER MANAGER ERROR ===');
  console.error('Error:', error);
  console.error('=======================');
});

export const onSigninCallback = async () => {
  console.log('Signin callback triggered');
  console.log('Current URL:', window.location.href);
  console.log('Current pathname:', window.location.pathname);
  
  try {
    // For react-oidc-context, we need to let it handle the callback
    // The library will automatically process the response
    console.log('Letting react-oidc-context handle the callback');
    
    // Check if we have the required parameters
    const urlParams = new URLSearchParams(window.location.search);
    const code = urlParams.get('code');
    const state = urlParams.get('state');
    const error = urlParams.get('error');
    
    console.log('Callback parameters:', {
      code: code ? code.substring(0, 20) + '...' : 'None',
      state: state || 'None',
      error: error || 'None'
    });
    
    // Clean up the URL by removing auth parameters
    window.history.replaceState({}, document.title, window.location.pathname);
    console.log('URL cleaned up');
  } catch (error) {
    console.error('Error in signin callback:', error);
  }
};

export const queryClient = new QueryClient();