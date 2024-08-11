import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TrainingJournalComponent } from './training-journal.component';

describe('TrainingJournalComponent', () => {
  let component: TrainingJournalComponent;
  let fixture: ComponentFixture<TrainingJournalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [TrainingJournalComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(TrainingJournalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
