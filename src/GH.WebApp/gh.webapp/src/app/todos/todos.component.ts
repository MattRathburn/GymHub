import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { NgIf, NgFor, AsyncPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BehaviorSubject, catchError, filter, Observable, of, Subject, switchMap, tap } from 'rxjs';
import { AuthenticationService } from '../authentication.service';
import { Todo } from '../../types/todo';

@Component({
    selector: 'app-todos',
    standalone: true,
    imports: [NgIf, NgFor, AsyncPipe, FormsModule],
    templateUrl: './todos.component.html',
    styleUrl: './todos.component.css'
})
export class TodosComponent {
  private readonly todos = new BehaviorSubject<Todo[]>([]);
  public readonly todos$: Observable<Todo[]> = this.todos;

  private readonly errors = new BehaviorSubject<string>('');
  public readonly error$: Observable<string> = this.errors;

  public authenticated$: Observable<boolean>;
  public anonymous$: Observable<boolean>;

  public date = (new Date()).toISOString().split('T')[0];
  public name = "";

  public constructor(
    private http: HttpClient,
    private auth: AuthenticationService) {
      this.authenticated$ = auth.getIsAuthenticated();
      this.anonymous$ = auth.getIsAnonymous();
    }

  public ngOnInit(): void {
    this.authenticated$
      .pipe(
        filter(isAuthenticated => isAuthenticated),
        tap(() => {
          this.fetchTodos();
        })
      ).subscribe();
  }

  public createTodo(): void {
    this.http
      .post<Todo>('todos', {
        name: this.name,
        date: this.date,
      }
      )
      .pipe(catchError(this.showError))
      .subscribe((todo) => {
        const todos = [...this.todos.getValue(), todo];
        this.todos.next(todos);
      });
  }

  public deleteTodo(id: number): void {
    this.http.delete(`todos/${id}`)
      .pipe(catchError(this.showError))
      .subscribe(() => {
        const todos = this.todos.getValue().filter((x) => x.id !== id);
        this.todos.next(todos);
      });
  }

  private fetchTodos(): void {
    this.http
      .get<Todo[]>('todos')
      .pipe(catchError(this.showError))
      .subscribe((todos) => {
        this.todos.next(todos);
      });
  }

  private readonly showError = (err: HttpErrorResponse) => {
    if (err.status !== 401) {
      this.errors.next(err.message);
    }
    throw err;
  }
}
