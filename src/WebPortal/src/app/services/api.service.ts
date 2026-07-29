import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private baseUrl = 'http://localhost:5001'; 

  constructor(private http: HttpClient) {}

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('token') || '';
    return new HttpHeaders({
      'Authorization': `Bearer ${token}`
    });
  }

  login(credentials: any): Observable<any> {
    return this.http.post(`${this.baseUrl}/api/v1/identity/login`, credentials);
  }

  register(user: any): Observable<any> {
    return this.http.post(`${this.baseUrl}/api/v1/identity/register`, user);
  }

  getDashboardStats(): Observable<any> {
    return this.http.get(`${this.baseUrl}/api/v1/reports/dashboard`, { headers: this.getHeaders() });
  }

  getCases(): Observable<any> {
    return this.http.get(`${this.baseUrl}/api/v1/cases`, { headers: this.getHeaders() });
  }

  createCase(caseData: any): Observable<any> {
    return this.http.post(`${this.baseUrl}/api/v1/cases`, caseData, { headers: this.getHeaders() });
  }

  addCaseNote(caseId: string, note: string): Observable<any> {
    return this.http.post(`${this.baseUrl}/api/v1/cases/${caseId}/notes`, { eventDescription: note }, { headers: this.getHeaders() });
  }

  getDocuments(): Observable<any> {
    return this.http.get(`${this.baseUrl}/api/v1/documents`, { headers: this.getHeaders() });
  }

  uploadDocument(formData: FormData): Observable<any> {
    return this.http.post(`${this.baseUrl}/api/v1/documents`, formData, { headers: this.getHeaders() });
  }

  search(query: string, type?: string): Observable<any> {
    let url = `${this.baseUrl}/api/v1/search?q=${encodeURIComponent(query)}`;
    if (type) {
      url += `&type=${encodeURIComponent(type)}`;
    }
    return this.http.get(url, { headers: this.getHeaders() });
  }

  getAuditLogs(filters: any): Observable<any> {
    let url = `${this.baseUrl}/api/v1/audit?`;
    if (filters.action) url += `action=${encodeURIComponent(filters.action)}&`;
    if (filters.entityType) url += `entityType=${encodeURIComponent(filters.entityType)}&`;
    if (filters.entityId) url += `entityId=${encodeURIComponent(filters.entityId)}&`;
    return this.http.get(url, { headers: this.getHeaders() });
  }

  exportAuditLogs(format: string): Observable<any> {
    return this.http.post(`${this.baseUrl}/api/v1/audit/export`, { format }, { headers: this.getHeaders(), responseType: 'text' });
  }
}
