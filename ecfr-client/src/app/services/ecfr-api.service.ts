import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
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
  private readonly ecfrBaseUrl = environment.ecfrBaseUrl;
  private cache = new Map<string, Observable<any>>();

  constructor(private http: HttpClient) {}

  private handleError<T>(operation = 'operation') {
    return (error: any): Observable<T> => {
      console.error(`${operation} failed:`, error);
      return of({} as T);
    };
  }

  private cacheRequest<T>(key: string, request: Observable<T>): Observable<T> {
    if (!this.cache.has(key)) {
      this.cache.set(
        key,
        request.pipe(
          shareReplay(1),
          catchError(this.handleError<T>())
        )
      );
    }
    return this.cache.get(key)!;
  }

  getTitles(): Observable<TitlesResponse> {
    const url = `${this.baseUrl}/ecfr/titles`;
    return this.cacheRequest<TitlesResponse>('titles', this.http.get<TitlesResponse>(url));
  }

  getAgencies(): Observable<AgenciesResponse> {
    const url = `${this.baseUrl}/ecfr/agencies`;
    return this.cacheRequest<AgenciesResponse>('agencies', this.http.get<AgenciesResponse>(url));
  }

  getAgencyBySlug(slug: string): Observable<Agency> {
    const url = `${this.baseUrl}/ecfr/agencies/${slug}`;
    return this.cacheRequest<Agency>(`agency-${slug}`, this.http.get<Agency>(url));
  }

  getAgencyTitles(slug: string): Observable<AgencyTitlesResult> {
    const url = `${this.baseUrl}/ecfr/agencies/${slug}/titles`;
    return this.cacheRequest<AgencyTitlesResult>(
      `agency-titles-${slug}`,
      this.http.get<AgencyTitlesResult>(url)
    );
  }

  getTitleXml(titleNumber: number): Observable<string> {
    const url = `${this.baseUrl}/ecfr/titles/${titleNumber}/xml`;
    return this.http.get(url, { responseType: 'text' });
  }

  getAgencyWordCountHistory(
    slug: string,
    startDate?: Date,
    endDate?: Date
  ): Observable<AgencyWordCountHistory> {
    let params = new HttpParams();
    if (startDate) {
      params = params.set('startDate', startDate.toISOString());
    }
    if (endDate) {
      params = params.set('endDate', endDate.toISOString());
    }

    const url = `${this.baseUrl}/ecfr/agencies/${slug}/word-count-history`;
    return this.http.get<AgencyWordCountHistory>(url, { params });
  }
}
