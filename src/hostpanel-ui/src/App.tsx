import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './contexts/AuthContext';
import { ThemeProvider } from './contexts/ThemeContext';
import { ProtectedRoute } from './components/ProtectedRoute';
import { Sidebar } from './components/Sidebar';
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

function AppLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen bg-gray-100 dark:bg-gray-900">
      <Sidebar />
      <main className="flex-1 p-8 md:ml-0 ml-0 dark:text-gray-100">{children}</main>
    </div>
  );
}

function P({ children }: { children: React.ReactNode }) {
  return <ProtectedRoute><AppLayout>{children}</AppLayout></ProtectedRoute>;
}

function App() {
  return (
    <ThemeProvider>
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<P><DashboardPage /></P>} />
          <Route path="/domains" element={<P><DomainsPage /></P>} />
          <Route path="/email" element={<P><EmailPage /></P>} />
          <Route path="/dns" element={<P><DnsPage /></P>} />
          <Route path="/databases" element={<P><DatabasesPage /></P>} />
          <Route path="/files" element={<P><FilesPage /></P>} />
          <Route path="/ssl" element={<P><SslPage /></P>} />
          <Route path="/billing" element={<P><BillingPage /></P>} />
          <Route path="/customers" element={<P><CustomersPage /></P>} />
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
          <Route path="/sso-callback" element={<SsoCallbackPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
    </ThemeProvider>
  );
}

export default App;
