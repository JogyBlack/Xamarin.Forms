﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Xamarin.Forms.Platform.iOS
{
	public class CarouselViewController : ItemsViewController<CarouselView>
	{
		protected readonly CarouselView Carousel;

		CarouselViewLoopManager _carouselViewLoopManager;
		bool _initialPositionSet;
		bool _viewInitialized;
		List<View> _oldViews;
		int _gotoPosition = -1;
		CGSize _size;
		ILoopItemsViewSource LoopItemsSource => ItemsSource as ILoopItemsViewSource;

		public CarouselViewController(CarouselView itemsView, ItemsViewLayout layout) : base(itemsView, layout)
		{
			Carousel = itemsView;
			CollectionView.AllowsSelection = false;
			CollectionView.AllowsMultipleSelection = false;
			Carousel.PropertyChanged += CarouselViewPropertyChanged;
			Carousel.Scrolled += CarouselViewScrolled;
			_oldViews = new List<View>();
		}

		public override UICollectionViewCell GetCell(UICollectionView collectionView, NSIndexPath indexPath)
		{
			UICollectionViewCell cell;
			if (Carousel?.Loop == true && _carouselViewLoopManager != null)
			{
				var cellAndCorrectedIndex = _carouselViewLoopManager.GetCellAndCorrectIndex(collectionView, indexPath, DetermineCellReuseId());
				cell = cellAndCorrectedIndex.cell;
				var correctedIndexPath = NSIndexPath.FromRowSection(cellAndCorrectedIndex.correctedIndex, 0);
				UpdateTemplatedCell(cell as TemplatedCell, correctedIndexPath);
			}
			else
			{
				cell = base.GetCell(collectionView, indexPath);
			}

			var element = (cell as TemplatedCell)?.VisualElementRenderer?.Element;
			if (element != null)
				VisualStateManager.GoToState(element, CarouselView.DefaultItemVisualState);
			return cell;
		}

		public override nint GetItemsCount(UICollectionView collectionView, nint section) => LoopItemsSource.LoopCount;

		public override void ViewDidLoad()
		{
			_carouselViewLoopManager = new CarouselViewLoopManager(Layout as UICollectionViewFlowLayout);
			base.ViewDidLoad();
		}

		public override void ViewWillLayoutSubviews()
		{
			base.ViewWillLayoutSubviews();
			if (!_viewInitialized)
			{
				_viewInitialized = true;
				_size = CollectionView.Bounds.Size;
			}

			UpdateVisualStates();
		}

		public override void ViewDidLayoutSubviews()
		{
			base.ViewDidLayoutSubviews();
			if (CollectionView.Bounds.Size != _size)
			{
				_size = CollectionView.Bounds.Size;
				BoundsSizeChanged();
			}

			if (Carousel?.Loop == true && _carouselViewLoopManager != null)
			{
				_carouselViewLoopManager.CenterIfNeeded(CollectionView, IsHorizontal);
			}
			UpdateInitialPosition();
		}

		public override void DraggingStarted(UIScrollView scrollView)
		{
			Carousel.SetIsDragging(true);
		}

		public override void DraggingEnded(UIScrollView scrollView, bool willDecelerate)
		{
			Carousel.SetIsDragging(false);
		}

		public override void UpdateItemsSource()
		{
			UnsubscribeCollectionItemsSourceChanged(ItemsSource);
			base.UpdateItemsSource();

			_carouselViewLoopManager?.SetItemsSource(LoopItemsSource);
			SubscribeCollectionItemsSourceChanged(ItemsSource);
			_initialPositionSet = false;
			UpdateInitialPosition();
		}

		protected override bool IsHorizontal => (Carousel?.ItemsLayout)?.Orientation == ItemsLayoutOrientation.Horizontal;

		protected override UICollectionViewDelegateFlowLayout CreateDelegator() => new CarouselViewDelegator(ItemsViewLayout, this);

		protected override string DetermineCellReuseId()
		{
			if (Carousel.ItemTemplate != null)
				return CarouselTemplatedCell.ReuseId;

			return base.DetermineCellReuseId();
		}

		protected override void RegisterViewTypes()
		{
			CollectionView.RegisterClassForCell(typeof(CarouselTemplatedCell), CarouselTemplatedCell.ReuseId);
			base.RegisterViewTypes();
		}

		protected override IItemsViewSource CreateItemsViewSource()
		{
			var itemsSource = ItemsSourceFactory.CreateForCarouselView(Carousel.ItemsSource, this, Carousel.Loop);
			_carouselViewLoopManager?.SetItemsSource(itemsSource);
			SubscribeCollectionItemsSourceChanged(itemsSource);
			return itemsSource;
		}

		protected void BoundsSizeChanged()
		{
			ItemsViewLayout.ConstrainTo(CollectionView.Bounds.Size);

			//We call ReloadData so our VisibleCells also update their size
			CollectionView.ReloadData();

			Carousel.ScrollTo(Carousel.Position, position: Xamarin.Forms.ScrollToPosition.Center, animate: false);
		}

		internal void TearDown()
		{
			Carousel.PropertyChanged -= CarouselViewPropertyChanged;
			Carousel.Scrolled -= CarouselViewScrolled;
			UnsubscribeCollectionItemsSourceChanged(ItemsSource);
			_carouselViewLoopManager?.Dispose();
			_carouselViewLoopManager = null;
		}

		internal void UpdateIsScrolling(bool isScrolling) => Carousel.IsScrolling = isScrolling;

		internal NSIndexPath GetScrollToIndexPath(int position)
		{
			if (Carousel?.Loop == true && _carouselViewLoopManager != null)
				return _carouselViewLoopManager.GetGoToIndex(CollectionView, position);

			return NSIndexPath.FromItemSection(position, 0);
		}

		void CarouselViewScrolled(object sender, ItemsViewScrolledEventArgs e)
		{
			var index = e.CenterItemIndex;
			if (Carousel?.Loop == true)
			{
				var cell = CollectionView.CellForItem(NSIndexPath.FromItemSection(e.CenterItemIndex, 0));
				if (cell is TemplatedCell templatedCell)
				{
					var bContext = templatedCell.VisualElementRenderer?.Element?.BindingContext;
					index = ItemsSource.GetIndexForItem(bContext).Row;
			
					SetPosition(index);
				}
			}
			else
			{
				SetPosition(index);
			}

			UpdateVisualStates();
		}

		void CollectionItemsSourceChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			var carouselPosition = Carousel.Position;
			var currentItemPosition = ItemsSource.GetIndexForItem(Carousel.CurrentItem).Row;
			var count = ItemsSource.ItemCount;

			bool removingCurrentElement = currentItemPosition == -1;
			bool removingLastElement = e.OldStartingIndex == count;
			bool removingFirstElement = e.OldStartingIndex == 0;
			bool removingCurrentElementButNotFirst = removingCurrentElement && removingLastElement && Carousel.Position > 0;

			if (removingCurrentElementButNotFirst)
			{
				carouselPosition = Carousel.Position - 1;
			}
			else if (removingFirstElement && !removingCurrentElement)
			{
				carouselPosition = currentItemPosition;
			}

			if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
			{
				carouselPosition = 0;
			}

			//If we are adding a new item make sure to maintain the CurrentItemPosition
			else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
				&& currentItemPosition != -1)
			{
				carouselPosition = currentItemPosition;
			}

			_gotoPosition = -1;

			SetCurrentItem(carouselPosition);
			SetPosition(carouselPosition);
			Carousel.ScrollTo(carouselPosition, position: Xamarin.Forms.ScrollToPosition.Center, animate: false);

		}

		void SubscribeCollectionItemsSourceChanged(IItemsViewSource itemsSource)
		{
			if (itemsSource is ObservableItemsSource newItemsSource)
				newItemsSource.CollectionItemsSourceChanged += CollectionItemsSourceChanged;
		}

		void UnsubscribeCollectionItemsSourceChanged(IItemsViewSource oldItemsSource)
		{
			if (oldItemsSource is ObservableItemsSource oldObservableItemsSource)
				oldObservableItemsSource.CollectionItemsSourceChanged -= CollectionItemsSourceChanged;
		}

		void CarouselViewPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs changedProperty)
		{
			if (changedProperty.Is(CarouselView.PositionProperty))
				UpdateFromPosition();
			else if (changedProperty.Is(CarouselView.CurrentItemProperty))
				UpdateFromCurrentItem();
			else if (changedProperty.Is(CarouselView.LoopProperty))
				UpdateLoop();
		}

		void UpdateLoop()
		{
			var carouselPosition = Carousel.Position;

			if(LoopItemsSource != null)
				LoopItemsSource.Loop = Carousel.Loop;

			CollectionView.ReloadData();

			ScrollToPosition(carouselPosition, carouselPosition, false, true);
		}

		void ScrollToPosition(int goToPosition, int carouselPosition, bool animate, bool forceScroll = false)
		{
			if (_gotoPosition == -1 && (goToPosition != carouselPosition || forceScroll))
			{
				_gotoPosition = goToPosition;
				Carousel.ScrollTo(goToPosition, position: Xamarin.Forms.ScrollToPosition.Center, animate: animate);
			}
		}

		void SetPosition(int position)
		{
			var carouselPosition = Carousel.Position;
			//we arrived center
			if (position == _gotoPosition)
				_gotoPosition = -1;

			if (_gotoPosition == -1 && carouselPosition != position)
			{
				Carousel.SetValueFromRenderer(CarouselView.PositionProperty, position);
			}
		}

		void SetCurrentItem(int carouselPosition)
		{
			if (ItemsSource.ItemCount == 0)
				return;

			var item = GetItemAtIndex(NSIndexPath.FromItemSection(carouselPosition, 0));
			Carousel.SetValueFromRenderer(CarouselView.CurrentItemProperty, item);
			UpdateVisualStates();
		}

		void UpdateFromCurrentItem()
		{
			if (Carousel.CurrentItem == null)
				return;

			var currentItemPosition = GetIndexForItem(Carousel.CurrentItem).Row;

			ScrollToPosition(currentItemPosition, Carousel.Position, Carousel.AnimateCurrentItemChanges);

			UpdateVisualStates();
		}

		void UpdateFromPosition()
		{
			var currentItemPosition = GetIndexForItem(Carousel.CurrentItem).Row;
			var carouselPosition = Carousel.Position;
			if (carouselPosition == _gotoPosition)
				_gotoPosition = -1;

			if (!Carousel.IsDragging || carouselPosition != -1)
				ScrollToPosition(carouselPosition, currentItemPosition, Carousel.AnimatePositionChanges);

			SetCurrentItem(carouselPosition);
		}

		void UpdateInitialPosition()
		{
			var itemsCount = ItemsSource?.ItemCount;

			if (itemsCount == 0)
				return;

			if (!_initialPositionSet)
			{
				_initialPositionSet = true;

				int position = Carousel.Position;
				var currentItem = Carousel.CurrentItem;
				if (currentItem != null)
				{
					position = ItemsSource.GetIndexForItem(currentItem).Row;
					SetPosition(position);
				}
				else
				{
					SetCurrentItem(position);
				}

				Carousel.ScrollTo(position, -1, Xamarin.Forms.ScrollToPosition.Center, false);
			}

			UpdateVisualStates();
		}

		void UpdateVisualStates()
		{
			var cells = CollectionView.VisibleCells;

			var newViews = new List<View>();

			var carouselPosition = Carousel.Position;
			var previousPosition = carouselPosition - 1;
			var nextPosition = carouselPosition + 1;

			foreach (var cell in cells)
			{
				if (!((cell as CarouselTemplatedCell)?.VisualElementRenderer?.Element is View itemView))
					return;

				var item = itemView.BindingContext;
				var pos = ItemsSource.GetIndexForItem(item).Row;

				if (pos == carouselPosition)
				{
					VisualStateManager.GoToState(itemView, CarouselView.CurrentItemVisualState);
				}
				else if (pos == previousPosition)
				{
					VisualStateManager.GoToState(itemView, CarouselView.PreviousItemVisualState);
				}
				else if (pos == nextPosition)
				{
					VisualStateManager.GoToState(itemView, CarouselView.NextItemVisualState);
				}
				else
				{
					VisualStateManager.GoToState(itemView, CarouselView.DefaultItemVisualState);
				}

				newViews.Add(itemView);

				if (!Carousel.VisibleViews.Contains(itemView))
				{
					Carousel.VisibleViews.Add(itemView);
				}
			}

			foreach (var itemView in _oldViews)
			{
				if (!newViews.Contains(itemView))
				{
					VisualStateManager.GoToState(itemView, CarouselView.DefaultItemVisualState);
					if (Carousel.VisibleViews.Contains(itemView))
					{
						Carousel.VisibleViews.Remove(itemView);
					}
				}
			}

			_oldViews = newViews;
		}
	}

	class CarouselViewLoopManager : IDisposable
	{
		int _indexOffset = 0;
		UICollectionViewFlowLayout _layout;
		const int LoopCount = 3;
		ILoopItemsViewSource _itemsSource;
		bool _disposed;

		public CarouselViewLoopManager(UICollectionViewFlowLayout layout)
		{
			if (layout == null)
				throw new ArgumentNullException(nameof(layout), "LoopManager expects a UICollectionViewFlowLayout");

			_layout = layout;
		}

		public void CenterIfNeeded(UICollectionView collectionView, bool isHorizontal)
		{
			if (isHorizontal)
				CenterHorizontalIfNeeded(collectionView);
			else
				CenterVerticallyIfNeeded(collectionView);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				if (disposing)
				{
					_itemsSource = null;
				}

				_disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		public (UICollectionViewCell cell, int correctedIndex) GetCellAndCorrectIndex(UICollectionView collectionView, NSIndexPath indexPath, string reuseId)
		{
			var cell = collectionView.DequeueReusableCell(reuseId, indexPath) as UICollectionViewCell;
			var correctedIndex = GetCorrectedIndexFromIndexPath(indexPath);
			return (cell, correctedIndex);
		}

		public int GetCorrectedIndexFromIndexPath(NSIndexPath indexPath)
		{
			return GetCorrectedIndex(indexPath.Row - _indexOffset);
		}

		public NSIndexPath GetGoToIndex(UICollectionView collectionView, int newPosition)
		{
			NSIndexPath centerIndexPath = GetIndexPathForCenteredItem(collectionView);
			if (centerIndexPath == null)
				return NSIndexPath.FromItemSection(0, 0);

			var currentCarouselPosition = GetCorrectedIndexFromIndexPath(centerIndexPath);
			var itemSourceCount = _itemsSource.ItemCount;

			var diffToStart = currentCarouselPosition + (itemSourceCount - newPosition);
			var diffToEnd = itemSourceCount - currentCarouselPosition + newPosition;

			var increment = currentCarouselPosition - newPosition;
			var incrementAbs = Math.Abs(increment);

			int goToPosition;
			if (diffToStart < incrementAbs)
				goToPosition = centerIndexPath.Row - diffToStart;
			else if (diffToEnd < incrementAbs)
				goToPosition = centerIndexPath.Row + diffToEnd;
			else
				goToPosition = centerIndexPath.Row - increment;

			NSIndexPath goToIndexPath = NSIndexPath.FromItemSection(goToPosition, 0);

			return goToIndexPath;
		}

		public void SetItemsSource(ILoopItemsViewSource itemsSource) => _itemsSource = itemsSource;

		void CenterVerticallyIfNeeded(UICollectionView collectionView)
		{
			var cellHeight = _layout.ItemSize.Height;
			var cellPadding = 0;
			var currentOffset = collectionView.ContentOffset;
			var contentHeight = GetTotalContentHeight();
			var boundsHeight = collectionView.Bounds.Size.Height;

			if (contentHeight == 0 || cellHeight == 0)
				return;

			var centerOffsetY = (LoopCount * contentHeight - boundsHeight) / 2;
			var distFromCenter = centerOffsetY - currentOffset.Y;

			if (Math.Abs(distFromCenter) > (contentHeight / 4))
			{
				var cellcount = distFromCenter / (cellHeight + cellPadding);
				var shiftCells = (int)((cellcount > 0) ? Math.Floor(cellcount) : Math.Ceiling(cellcount));
				var offsetCorrection = (Math.Abs(cellcount) % 1.0) * (cellHeight + cellPadding);

				if (collectionView.ContentOffset.Y < centerOffsetY)
				{
					collectionView.ContentOffset = new CGPoint(currentOffset.X, centerOffsetY - offsetCorrection);
				}
				else if (collectionView.ContentOffset.Y > centerOffsetY)
				{
					collectionView.ContentOffset = new CGPoint(currentOffset.X, centerOffsetY + offsetCorrection);
				}

				FinishCenterIfNeeded(collectionView,shiftCells);
			}
		}

		void CenterHorizontalIfNeeded(UICollectionView collectionView)
		{
			var cellWidth = _layout.ItemSize.Width;
			var cellPadding = 0;
			var currentOffset = collectionView.ContentOffset;
			var contentWidth = GetTotalContentWidth();
			var boundsWidth = collectionView.Bounds.Size.Width;

			if (contentWidth == 0 || cellWidth == 0)
				return;

			var centerOffsetX = (LoopCount * contentWidth - boundsWidth) / 2;
			var distFromCentre = centerOffsetX - currentOffset.X;

			if (Math.Abs(distFromCentre) > (contentWidth / 4))
			{
				var cellcount = distFromCentre / (cellWidth + cellPadding);
				var shiftCells = (int)((cellcount > 0) ? Math.Floor(cellcount) : Math.Ceiling(cellcount));
				var offsetCorrection = (Math.Abs(cellcount % 1.0f)) * (cellWidth + cellPadding);

				if (collectionView.ContentOffset.X < centerOffsetX)
				{
					collectionView.ContentOffset = new CGPoint(centerOffsetX - offsetCorrection, currentOffset.Y);
				}
				else if (collectionView.ContentOffset.X > centerOffsetX)
				{
					collectionView.ContentOffset = new CGPoint(centerOffsetX + offsetCorrection, currentOffset.Y);
				}

				FinishCenterIfNeeded(collectionView, shiftCells);
			}

		}

		void FinishCenterIfNeeded(UICollectionView collectionView, int shiftCells)
		{
			ShiftContentArray(shiftCells);

			collectionView.ReloadData();
		}

		int GetCorrectedIndex(int indexToCorrect)
		{
			var itemsCount = GetItemsSourceCount();
			if ((indexToCorrect < itemsCount && indexToCorrect >= 0) || itemsCount == 0)
				return indexToCorrect;

			var countInIndex = (double)(indexToCorrect / itemsCount);
			var flooredValue = (int)(Math.Floor(countInIndex));
			var offset = itemsCount * flooredValue;
			var newIndex = indexToCorrect - offset;
			if (newIndex < 0)
				return (itemsCount - Math.Abs(newIndex));
			return newIndex;
		}

		NSIndexPath GetIndexPathForCenteredItem(UICollectionView collectionView)
		{
			var centerPoint = new CGPoint(collectionView.Center.X + collectionView.ContentOffset.X, collectionView.Center.Y + collectionView.ContentOffset.Y);
			var centerIndexPath = collectionView.IndexPathForItemAtPoint(centerPoint);
			return centerIndexPath;
		}

		int GetItemsSourceCount() => _itemsSource.ItemCount;

		nfloat GetTotalContentWidth() => GetItemsSourceCount() * _layout.ItemSize.Width;

		nfloat GetTotalContentHeight() => GetItemsSourceCount() * _layout.ItemSize.Height;

		void ShiftContentArray(int shiftCells)
		{
			var correctedIndex = GetCorrectedIndex(shiftCells);
			_indexOffset += correctedIndex;
		}
	}
}
