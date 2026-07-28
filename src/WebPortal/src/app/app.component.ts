import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from './services/api.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {
  title = 'Legal Platform Admin Portal';
  activeTab: string = 'dashboard';
  isAuthenticated: boolean = false;
  loading: boolean = false;
  errorMsg: string = '';

  // Auth fields
  emailInput: string = 'admin@legal.com';
  passwordInput: string = 'devpassword123';
  selectedRole: string = 'SystemAdmin';
  userEmail: string = '';
  userRole: string = '';
  userTenantId: string = '';

  // Dashboard / Reports metrics
  workflowsAssigned: number = 0;
  workflowsCompleted: number = 0;
  completionRate: number = 0;
  lastUpdated: string = '';

  // Cases state
  cases: any[] = [];
  selectedCase: any = null;
  newCaseTitle: string = '';
  newCaseDescription: string = '';
  newCaseNumber: string = '';
  newCaseDeadline: string = '';
  newTimelineNote: string = '';

  // Documents state
  documents: any[] = [];
  selectedDocument: any = null;
  newDocumentTitle: string = '';
  newDocumentCategory: string = 'Contract';
  selectedFile: File | null = null;

  // Search state
  searchQuery: string = '';
  searchTypeFilter: string = '';
  searchResults: any[] = [];

  // Audit Logs state
  auditLogs: any[] = [];
  auditActionFilter: string = '';
  auditTypeFilter: string = '';
  exportedLogs: string = '';

  constructor(private apiService: ApiService) {}

  ngOnInit() {
    const savedToken = localStorage.getItem('token');
    if (savedToken) {
      this.userEmail = localStorage.getItem('userEmail') || '';
      this.userRole = localStorage.getItem('userRole') || '';
      this.userTenantId = localStorage.getItem('userTenant') || '';
      this.isAuthenticated = true;
      this.loadActiveTabData();
    }
  }

  login() {
    this.loading = true;
    this.errorMsg = '';
    
    const credentials = { email: this.emailInput, password: this.passwordInput };
    this.apiService.login(credentials).subscribe({
      next: (res: any) => {
        this.processAuthSuccess(res.accessToken, this.emailInput);
      },
      error: () => {
        const registerData = {
          email: this.emailInput,
          password: this.passwordInput,
          firstName: 'Simulated',
          lastName: this.selectedRole,
          organizationId: '3fa85f64-5717-4562-b3fc-2c963f66afa6'
        };
        this.apiService.register(registerData).subscribe({
          next: () => {
            this.apiService.login(credentials).subscribe({
              next: (loginRes: any) => {
                this.processAuthSuccess(loginRes.accessToken, this.emailInput);
              },
              error: (err) => this.handleError(err)
            });
          },
          error: (err) => this.handleError(err)
        });
      }
    });
  }

  private processAuthSuccess(accessToken: string, email: string) {
    localStorage.setItem('token', accessToken);
    
    const decoded = this.parseJwt(accessToken);
    const tenantId = decoded?.organizationId || decoded?.tenant_id || '3fa85f64-5717-4562-b3fc-2c963f66afa6';
    
    localStorage.setItem('userEmail', email);
    localStorage.setItem('userRole', this.selectedRole);
    localStorage.setItem('userTenant', tenantId);

    this.userEmail = email;
    this.userRole = this.selectedRole;
    this.userTenantId = tenantId;
    this.isAuthenticated = true;
    this.loading = false;
    this.loadActiveTabData();
  }

  private parseJwt(token: string): any {
    try {
      const base64Url = token.split('.')[1];
      const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
      const jsonPayload = decodeURIComponent(window.atob(base64).split('').map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2)).join(''));
      return JSON.parse(jsonPayload);
    } catch {
      return null;
    }
  }

  logout() {
    localStorage.clear();
    this.isAuthenticated = false;
    this.cases = [];
    this.documents = [];
    this.searchResults = [];
    this.auditLogs = [];
  }

  setTab(tab: string) {
    this.activeTab = tab;
    if (this.isAuthenticated) {
      this.loadActiveTabData();
    }
  }

  loadActiveTabData() {
    this.loading = true;
    this.errorMsg = '';
    
    if (this.activeTab === 'dashboard') {
      this.apiService.getDashboardStats().subscribe({
        next: (res: any) => {
          this.workflowsAssigned = res.workflowsAssigned;
          this.workflowsCompleted = res.workflowsCompleted;
          this.completionRate = res.completionRate;
          this.lastUpdated = res.lastUpdated;
          this.loading = false;
        },
        error: (err) => this.handleError(err)
      });
    } else if (this.activeTab === 'cases') {
      this.apiService.getCases().subscribe({
        next: (res: any) => {
          this.cases = res;
          this.loading = false;
        },
        error: (err) => this.handleError(err)
      });
    } else if (this.activeTab === 'documents') {
      this.apiService.getDocuments().subscribe({
        next: (res: any) => {
          this.documents = res;
          this.loading = false;
        },
        error: (err) => this.handleError(err)
      });
    } else if (this.activeTab === 'audit') {
      this.loadAuditLogs();
    } else {
      this.loading = false;
    }
  }

  selectCase(kase: any) {
    this.selectedCase = kase;
  }

  createCase() {
    if (!this.newCaseTitle || !this.newCaseNumber) return;
    this.loading = true;
    const caseData = {
      title: this.newCaseTitle,
      description: this.newCaseDescription,
      caseNumber: this.newCaseNumber,
      clientId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
      assignedLawyerId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
      deadline: this.newCaseDeadline ? new Date(this.newCaseDeadline).toISOString() : null
    };

    this.apiService.createCase(caseData).subscribe({
      next: () => {
        this.newCaseTitle = '';
        this.newCaseDescription = '';
        this.newCaseNumber = '';
        this.newCaseDeadline = '';
        this.loadActiveTabData();
      },
      error: (err) => this.handleError(err)
    });
  }

  addCaseNote() {
    if (!this.selectedCase || !this.newTimelineNote) return;
    this.loading = true;
    this.apiService.addCaseNote(this.selectedCase.id, this.newTimelineNote).subscribe({
      next: (note: any) => {
        if (!this.selectedCase.timelineEntries) {
          this.selectedCase.timelineEntries = [];
        }
        this.selectedCase.timelineEntries.push(note);
        this.newTimelineNote = '';
        this.loading = false;
      },
      error: (err) => this.handleError(err)
    });
  }

  onFileSelected(event: any) {
    this.selectedFile = event.target.files[0];
  }

  uploadDocument() {
    if (!this.newDocumentTitle || !this.selectedFile) return;
    this.loading = true;

    const formData = new FormData();
    formData.append('file', this.selectedFile);
    formData.append('title', this.newDocumentTitle);
    formData.append('category', this.newDocumentCategory);

    this.apiService.uploadDocument(formData).subscribe({
      next: () => {
        this.newDocumentTitle = '';
        this.selectedFile = null;
        this.loadActiveTabData();
      },
      error: (err) => this.handleError(err)
    });
  }

  selectDocument(doc: any) {
    this.selectedDocument = doc;
  }

  runSearch() {
    if (!this.searchQuery) return;
    this.loading = true;
    this.apiService.search(this.searchQuery, this.searchTypeFilter).subscribe({
      next: (res: any) => {
        this.searchResults = res;
        this.loading = false;
      },
      error: (err) => this.handleError(err)
    });
  }

  loadAuditLogs() {
    const filters = {
      action: this.auditActionFilter,
      entityType: this.auditTypeFilter
    };
    this.apiService.getAuditLogs(filters).subscribe({
      next: (res: any) => {
        this.auditLogs = res;
        this.loading = false;
      },
      error: (err) => this.handleError(err)
    });
  }

  exportAuditLogs(format: string) {
    this.loading = true;
    this.apiService.exportAuditLogs(format).subscribe({
      next: (res: string) => {
        this.exportedLogs = res;
        this.loading = false;
        
        const blob = new Blob([res], { type: 'text/plain' });
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `audit-export.${format === 'csv' ? 'csv' : 'txt'}`;
        a.click();
        window.URL.revokeObjectURL(url);
        this.loadAuditLogs(); 
      },
      error: (err) => this.handleError(err)
    });
  }

  private handleError(err: any) {
    this.loading = false;
    if (err.status === 0) {
      this.errorMsg = 'Unable to connect to Gateway API (http://localhost:5000). Please ensure backend services are running.';
    } else if (err.status === 403) {
      this.errorMsg = 'Forbidden. Your current role does not have authorization to view this resource.';
    } else if (err.status === 429) {
      this.errorMsg = 'Too many requests. You have been rate limited by the API Gateway.';
    } else {
      this.errorMsg = err.error?.detail || err.message || 'An unexpected error occurred.';
    }
  }
}
