import styled from '@emotion/styled';
import type { FC, ReactNode } from 'react';
import { useAuth } from 'react-oidc-context';
import { useNavigate } from 'react-router-dom';
import { appRoutes } from '../constants.ts';
import { useEffect } from 'react';
import { userManager } from '../config.ts';

const Container = styled.div`
  margin-left: auto;
  margin-right: auto;
  padding-left: 0.5rem;
  padding-right: 0.5rem;
  max-width: 1200px;
`;

const NavBar = styled.nav`
  display: flex;
  align-items: center;
  padding-top: 1rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid;
`;

const NavTitle = styled.div`
  flex-grow: 1;
  font-weight: bold;
  font-style: italic;
`;

const NavButtons = styled.div`
  display: flex;
  gap: 1rem;
`;

const NavButton = styled.button`
  border-radius: 0.25rem;
  padding: 0.375rem 0.75rem;
  cursor: pointer;
  border: 1px solid #ccc;
  background-color: #f8f9fa;
  
  &:hover {
    background-color: #e9ecef;
  }
`;

const AuthButton = styled.button`
  border-radius: 0.25rem;
  padding: 0.375rem 0.75rem;
  cursor: pointer;
  border: 1px solid #007bff;
  background-color: #007bff;
  color: white;
  
  &:hover {
    background-color: #0056b3;
  }
`;

const Main = styled.main`
  margin-bottom: 2rem;
`;

type NavItemType = {
  text: string;
  /** Setting this flag to `true` means that only auth'd users should see the nav item */
  protected: boolean;
  action: () => void;
};

interface LayoutProps {
  children: ReactNode;
}

export const Layout: FC<LayoutProps> = (props) => {
  const { children } = props;
  const auth = useAuth();
  const navigate = useNavigate();

  // Debug logging
  console.log('Auth state:', {
    isAuthenticated: auth.isAuthenticated,
    isLoading: auth.isLoading,
    user: auth.user,
    error: auth.error,
    activeNavigator: auth.activeNavigator
  });

  const navItems: NavItemType[] = [
    {
      text: 'Home',
      protected: true,
      action: () => {
        navigate(appRoutes.home);
      },
    },
    {
      text: 'Playground',
      protected: true,
      action: () => {
        navigate(appRoutes.playground);
      },
    },
  ];

  const handleLogin = async () => {
    console.log('Initiating login...');
    try {
      // Check current state before login
      const currentUser = await userManager.getUser();
      console.log('Current user before login:', currentUser);
      
      await auth.signinRedirect();
    } catch (error) {
      console.error('Login error:', error);
    }
  };

  const handleLogout = async () => {
    console.log('Initiating logout...');
    try {
      await auth.signoutRedirect();
    } catch (error) {
      console.error('Logout error:', error);
    }
  };

  const checkAuthStatus = async () => {
    console.log('Checking auth status...');
    try {
      // Check if we can get the current user from the auth context
      if (auth.user) {
        console.log('Current user from auth context:', auth.user);
        console.log('User profile:', auth.user.profile);
      } else {
        console.log('No user in auth context');
      }
      
      // Also check the user manager directly
      try {
        const user = await userManager.getUser();
        console.log('User from userManager:', user);
        if (user) {
          console.log('User expired:', user.expired);
          console.log('User access token:', user.access_token ? 'Present' : 'None');
        }
      } catch (error) {
        console.error('Error getting user from userManager:', error);
      }
    } catch (error) {
      console.error('Error checking auth status:', error);
    }
  };

  // Check auth status on component mount
  useEffect(() => {
    checkAuthStatus();
    
    // Check if there are auth parameters in the URL
    const urlParams = new URLSearchParams(window.location.search);
    const hasAuthParams = urlParams.has('code') || urlParams.has('error');
    console.log('URL has auth params:', hasAuthParams);
    if (hasAuthParams) {
      console.log('Auth params found:', Object.fromEntries(urlParams.entries()));
    }
  }, []);

  // Filter navigation items based on authentication status
  const visibleNavItems = navItems.filter((item) => {
    return auth.isAuthenticated || !item.protected;
  });

  return (
    <Container>
      <NavBar>
        <NavTitle>Example App</NavTitle>
        <NavButtons>
          {visibleNavItems.map((item) => (
            <NavButton key={item.text} onClick={item.action}>
              {item.text}
            </NavButton>
          ))}
          
          {/* Authentication button */}
          {auth.isAuthenticated ? (
            <AuthButton onClick={handleLogout}>
              Logout
            </AuthButton>
          ) : (
            <AuthButton onClick={handleLogin}>
              Login
            </AuthButton>
          )}
          
          {/* Debug refresh button */}
          <NavButton onClick={checkAuthStatus} style={{ backgroundColor: '#ffc107' }}>
            Refresh Auth
          </NavButton>
          
          {/* Manual callback processing button */}
          <NavButton onClick={async () => {
            try {
              const user = await userManager.signinRedirectCallback();
              console.log('Manual callback processing result:', user);
            } catch (error) {
              console.error('Manual callback processing error:', error);
            }
          }} style={{ backgroundColor: '#28a745' }}>
            Process Callback
          </NavButton>
          
          {/* Check client config button */}
          <NavButton onClick={async () => {
            console.log('=== CLIENT CONFIGURATION ===');
            console.log('Authority:', userManager.settings.authority);
            console.log('Client ID:', userManager.settings.client_id);
            console.log('Redirect URI:', userManager.settings.redirect_uri);
            console.log('Response Type:', userManager.settings.response_type);
            console.log('Scope:', userManager.settings.scope);
            console.log('Metadata:', userManager.settings.metadata);
            console.log('========================');
          }} style={{ backgroundColor: '#17a2b8' }}>
            Check Config
          </NavButton>
          
          {/* Manual token exchange button */}
          <NavButton onClick={async () => {
            try {
              console.log('=== MANUAL TOKEN EXCHANGE ===');
              const urlParams = new URLSearchParams(window.location.search);
              const code = urlParams.get('code');
              const state = urlParams.get('state');
              
              if (code && state) {
                console.log('Authorization code found:', code.substring(0, 20) + '...');
                console.log('State found:', state);
                
                // Try to manually exchange the code for tokens
                const user = await userManager.signinRedirectCallback();
                console.log('Token exchange result:', user);
              } else {
                console.log('No authorization code found in URL');
              }
              console.log('============================');
            } catch (error) {
              console.error('Manual token exchange error:', error);
            }
          }} style={{ backgroundColor: '#6f42c1' }}>
            Exchange Token
          </NavButton>
        </NavButtons>
      </NavBar>

      {/* Debug info - remove this after fixing */}
      <div style={{ padding: '1rem', backgroundColor: '#f0f0f0', marginBottom: '1rem' }}>
        <strong>Debug Info:</strong>
        <br />
        isAuthenticated: {auth.isAuthenticated ? 'true' : 'false'}
        <br />
        isLoading: {auth.isLoading ? 'true' : 'false'}
        <br />
        User: {auth.user ? 'Present' : 'None'}
        <br />
        Error: {auth.error ? auth.error.message : 'None'}
        <br />
        Active Navigator: {auth.activeNavigator || 'None'}
        <br />
        <br />
        <strong>Session Storage:</strong>
        <br />
        {Object.keys(window.sessionStorage).filter(key => key.includes('oidc')).map(key => (
          <div key={key}>
            {key}: {window.sessionStorage.getItem(key)?.substring(0, 100)}...
          </div>
        ))}
      </div>

      <Main>{children}</Main>
    </Container>
  );
};