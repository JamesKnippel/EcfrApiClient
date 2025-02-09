import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface CfrReference {
  title: number;
  chapter: string;
  subtitle: string | null;
}

export interface Agency {
  name: string;
  short_name: string | null;
  display_name: string;
  sortable_name: string;
  slug: string;
  children: Agency[];
  cfr_references: CfrReference[];
}

export interface AgenciesResponse {
  agencies: Agency[];
}

export interface TitlesResponse {
  titles: Title[];
}

export interface Title {
  number: number;
  name: string;
}

export interface TitleWordCount {
  number: number;
  name: string;
  wordCount: number;
}

export interface AgencyTitlesResult {
  agency: Agency;
  titles: TitleWordCount[];
  totalWordCount: number;
}

export interface Meta {
  date: string;
  import_in_progress: boolean;
}

export interface TitleWordCountHistory {
  titleNumber: number;
  titleName: string;
  wordCounts: {
    date: string;
    wordCount: number;
    wordsAddedSinceLastSnapshot: number;
    daysSinceLastSnapshot: number;
    wordsPerDay: number;
  }[];
  totalWordsAdded: number;
  averageWordsPerDay: number;
}

export interface AgencyWordCountHistory {
  agency: Agency;
  titleHistories: TitleWordCountHistory[];
  startDate: string;  
  endDate: string;    
  totalWordsAdded: number;
  averageWordsPerDay: number;
}

@Injectable({
  providedIn: 'root'
})
export class EcfrApiService {
  private readonly baseUrl = environment.apiUrl;
  private cache = new Map<string, Observable<any>>();

  constructor(private http: HttpClient) {}

  getTitles(): Observable<TitlesResponse> {
    const cacheKey = 'titles';
    if (!this.cache.has(cacheKey)) {
      const request = this.http.get<TitlesResponse>(`${this.baseUrl}/titles`).pipe(
        shareReplay(1),
        catchError(error => {
          this.cache.delete(cacheKey);
          throw error;
        })
      );
      this.cache.set(cacheKey, request);
    }
    return this.cache.get(cacheKey)!;
  }

  getAgencies(): Observable<AgenciesResponse> {
    const cacheKey = 'agencies';
    if (!this.cache.has(cacheKey)) {
      const request = this.http.get<AgenciesResponse>(`${this.baseUrl}/agencies`).pipe(
        shareReplay(1),
        catchError(error => {
          this.cache.delete(cacheKey);
          throw error;
        })
      );
      this.cache.set(cacheKey, request);
    }
    return this.cache.get(cacheKey)!;
  }

  getAgencyBySlug(slug: string): Observable<Agency> {
    const cacheKey = `agency_${slug}`;
    if (!this.cache.has(cacheKey)) {
      const request = this.http.get<Agency>(`${this.baseUrl}/agencies/${slug}`).pipe(
        shareReplay(1),
        catchError(error => {
          this.cache.delete(cacheKey);
          throw error;
        })
      );
      this.cache.set(cacheKey, request);
    }
    return this.cache.get(cacheKey)!;
  }

  getAgencyTitles(slug: string): Observable<AgencyTitlesResult> {
    const cacheKey = `agency_titles_${slug}`;
    if (!this.cache.has(cacheKey)) {
      const request = this.http.get<AgencyTitlesResult>(`${this.baseUrl}/agencies/${slug}/titles`).pipe(
        shareReplay(1),
        catchError(error => {
          this.cache.delete(cacheKey);
          throw error;
        })
      );
      this.cache.set(cacheKey, request);
    }
    return this.cache.get(cacheKey)!;
  }

  getTitleXml(titleNumber: number): Observable<string> {
    const cacheKey = `title_xml_${titleNumber}`;
    if (!this.cache.has(cacheKey)) {
      const request = this.http.get(`${this.baseUrl}/title/${titleNumber}/xml`, { responseType: 'text' }).pipe(
        shareReplay(1),
        catchError(error => {
          this.cache.delete(cacheKey);
          throw error;
        })
      );
      this.cache.set(cacheKey, request);
    }
    return this.cache.get(cacheKey)!;
  }

  getAgencyWordCountHistory(slug: string, startDate?: Date, endDate?: Date): Observable<AgencyWordCountHistory> {
    let url = `${this.baseUrl}/agencies/${slug}/word-count-history`;
    const params = new URLSearchParams();
    
    if (startDate) {
      params.append('startDate', startDate.toISOString());
    }
    if (endDate) {
      params.append('endDate', endDate.toISOString());
    }
    
    const queryString = params.toString();
    if (queryString) {
      url += '?' + queryString;
    }

    return this.http.get<AgencyWordCountHistory>(url).pipe(
      map(history => ({
        ...history,
        startDate: history.startDate,  
        endDate: history.endDate       
      }))
    );
  }
}
