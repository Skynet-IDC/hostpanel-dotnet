import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useEffect } from 'react';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { BrandingProvider, useBranding } from './contexts/BrandingContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { Layout } from './components/shell/Layout';
import { SetupWizard } from './components/wizard/SetupWizard';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { DomainsPage } from './pages/DomainsPage';
import { FilesPage } from './pages/FilesPage';
import { EmailPage } from './pages/EmailPage';
import { DnsPage } from './pages/DnsPage';
import { DatabasesPage } from './pages/DatabasesPage';
import { SslPage } from './pages/SslPage';
import { BillingPage } from './pages/BillingPage';
import { CustomersPage } from './pages/CustomersPage';
import { PlansPage } from './pages/PlansPage';
import { SubscriptionsPage } from './pages/SubscriptionsPage';
import { ClaudeTerminalPage } from './pages/ClaudeTerminalPage';
import { BrandingPage } from './pages/BrandingPage';
import { WebhooksPage } from './pages/WebhooksPage';
import { TwoFactorPage } from './pages/TwoFactorPage';
import { Fail2banPage } from './pages/Fail2banPage';
import { MalwarePage } from './pages/MalwarePage';
import { AccountPage } from './pages/AccountPage';
import { FirewallPage } from './pages/FirewallPage';
import { CronPage } from './pages/CronPage';
import { ServerUpdatesPage } from './pages/ServerUpdatesPage';
import { AnalyticsPage } from './pages/AnalyticsPage';
import { SecurityPage } from './pages/SecurityPage';
import { AppsPage } from './pages/AppsPage';
import { WordPressPage } from './pages/WordPressPage';
import { DockerPage } from './pages/DockerPage';
import { GitDeployPage } from './pages/GitDeployPage';
import { SettingsPage } from './pages/SettingsPage';
import { SsoCallbackPage } from './pages/SsoCallbackPage';
import { BackupsPage } from './pages/BackupsPage';
import { FtpPage } from './pages/FtpPage';
import { IpManagementPage } from './pages/IpManagementPage';
import { HaDashboardPage } from './pages/HaDashboardPage';
import { AuditLogPage } from './pages/AuditLogPage';
import { MigrationPage } from './pages/MigrationPage';
import { LogsPage } from './pages/LogsPage';
import { ServersPage } from './pages/ServersPage';

/** Mirrors `AuthContext.user.role` (Admin/Reseller/Customer, capitalized)
 * into `BrandingContext.role` (admin/reseller/customer) so the sidebar nav
 * reflects who's logged in. Admin can later override in their Branding panel
 * to preview the reseller/customer view. */
function RoleSync() {
  const { user } = useAuth();
  const { role, setBranding } = useBranding();
  useEffect(() => {
    if (!user) return;
    const lower = user.role.toLowerCase() as 'admin' | 'reseller' | 'customer';
    if (lower !== role && (lower === 'admin' || lower === 'reseller' || lower === 'customer')) {
      setBranding({ role: lower });
    }
  }, [user]);
  return null;
}

/** Re-fetch branding on login/logout so the logged-in user's per-customer
 * override (if any) is applied to logo / panelName / primaryHue. */
function BrandingSync() {
  const { user } = useAuth();
  const { refresh } = useBranding();
  useEffect(() => { void refresh(); }, [user?.id]);
  return null;
}

/** Shows the first-launch wizard if branding hasn't been configured yet
 * AND the operator is logged in as Admin. Customers never see it. */
function WizardGate() {
  const { isAuthenticated, user } = useAuth();
  const { configured, loading } = useBranding();
  if (loading || !isAuthenticated) return null;
  if (user?.role !== 'Admin') return null;
  if (configured) return null;
  return <SetupWizard />;
}

function P({ children }: { children: React.ReactNode }) {
  return (
    <ProtectedRoute>
      <Layout>{children}</Layout>
    </ProtectedRoute>
  );
}

function App() {
  return (
    <BrandingProvider>
      <AuthProvider>
        <BrowserRouter>
          <RoleSync />
          <BrandingSync />
          <WizardGate />
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/sso-callback" element={<SsoCallbackPage />} />
            <Route path="/" element={<P><DashboardPage /></P>} />
            <Route path="/domains" element={<P><DomainsPage /></P>} />
            <Route path="/email" element={<P><EmailPage /></P>} />
            <Route path="/dns" element={<P><DnsPage /></P>} />
            <Route path="/databases" element={<P><DatabasesPage /></P>} />
            <Route path="/files" element={<P><FilesPage /></P>} />
            <Route path="/ssl" element={<P><SslPage /></P>} />
            <Route path="/billing" element={<P><BillingPage /></P>} />
            <Route path="/customers" element={<P><CustomersPage /></P>} />
            <Route path="/plans" element={<P><PlansPage /></P>} />
            <Route path="/subscriptions" element={<P><SubscriptionsPage /></P>} />
            <Route path="/claude" element={<P><ClaudeTerminalPage /></P>} />
            <Route path="/branding" element={<P><BrandingPage /></P>} />
            <Route path="/webhooks" element={<P><WebhooksPage /></P>} />
            <Route path="/fail2ban" element={<P><Fail2banPage /></P>} />
            <Route path="/malware" element={<P><MalwarePage /></P>} />
            <Route path="/2fa" element={<P><TwoFactorPage /></P>} />
            <Route path="/account" element={<P><AccountPage /></P>} />
            <Route path="/firewall" element={<P><FirewallPage /></P>} />
            <Route path="/cron" element={<P><CronPage /></P>} />
            <Route path="/updates" element={<P><ServerUpdatesPage /></P>} />
            <Route path="/analytics" element={<P><AnalyticsPage /></P>} />
            <Route path="/security" element={<P><SecurityPage /></P>} />
            <Route path="/apps" element={<P><AppsPage /></P>} />
            <Route path="/wordpress" element={<P><WordPressPage /></P>} />
            <Route path="/docker" element={<P><DockerPage /></P>} />
            <Route path="/git" element={<P><GitDeployPage /></P>} />
            <Route path="/settings" element={<P><SettingsPage /></P>} />
            <Route path="/backups" element={<P><BackupsPage /></P>} />
            <Route path="/ftp" element={<P><FtpPage /></P>} />
            <Route path="/ip" element={<P><IpManagementPage /></P>} />
            <Route path="/ha" element={<P><HaDashboardPage /></P>} />
            <Route path="/audit" element={<P><AuditLogPage /></P>} />
            <Route path="/migration" element={<P><MigrationPage /></P>} />
            <Route path="/logs" element={<P><LogsPage /></P>} />
            <Route path="/servers" element={<P><ServersPage /></P>} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </BrandingProvider>
  );
}

export default App;
